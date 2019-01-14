﻿using Dacs7.Alarms;
using Dacs7.Helper;
using Dacs7.Protocols.SiemensPlc;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Dacs7.Protocols
{
    internal partial class ProtocolHandler
    {
        private ConcurrentDictionary<ushort, CallbackHandler<S7PendingAlarmAckDatagram>> _alarmHandler = new ConcurrentDictionary<ushort, CallbackHandler<S7PendingAlarmAckDatagram>>();
        private CallbackHandler<S7AlarmUpdateAckDatagram> _alarmUpdateHandler = new CallbackHandler<S7AlarmUpdateAckDatagram>();
        private ConcurrentDictionary<ushort, CallbackHandler<S7AlarmIndicationDatagram>> _alarmIndicationHandler = new ConcurrentDictionary<ushort, CallbackHandler<S7AlarmIndicationDatagram>>();

        public async Task<IEnumerable<IPlcAlarm>> ReadPendingAlarmsAsync()
        {
            if (ConnectionState != ConnectionState.Opened)
                throw new Dacs7NotConnectedException();

            var id = GetNextReferenceId();
            var sequenceNumber = (byte)0x00;
            var alarms = new List<IPlcAlarm>();
            var memory = Memory<byte>.Empty;
            var currentPosition = 0;
            var totalLength = 0;
            try
            {

                S7PendingAlarmAckDatagram alarmResults = null;
                do
                {
                    var sendData = _transport.Build(S7UserDataDatagram.TranslateToMemory(S7UserDataDatagram.BuildPendingAlarmRequest(_s7Context, id, sequenceNumber)));

                    using (await SemaphoreGuard.Async(_concurrentJobs))
                    {
                        var cbh = new CallbackHandler<S7PendingAlarmAckDatagram>(id);
                        _alarmHandler.TryAdd(cbh.Id, cbh);
                        try
                        {
                            if (await _transport.Client.SendAsync(sendData) != SocketError.Success)
                                return null;

                            alarmResults = await cbh.Event.WaitAsync(_s7Context.Timeout);

                        }
                        finally
                        {
                            _alarmHandler.TryRemove(cbh.Id, out _);
                        }
                    }

                    if (alarmResults == null)
                    {
                        if (_closeCalled)
                        {
                            throw new Dacs7NotConnectedException();
                        }
                        else
                        {
                            throw new Dacs7ReadTimeoutException(id);
                        }
                    }

                    if (memory.IsEmpty)
                    {
                        totalLength = BinaryPrimitives.ReadUInt16BigEndian(alarmResults.UserData.Data.Data.Span.Slice(4, 2)) + 6; // 6 is the header
                        memory = ArrayPool<byte>.Shared.Rent(totalLength);
                    }

                    alarmResults.UserData.Data.Data.CopyTo(memory.Slice(currentPosition, alarmResults.UserData.Data.Data.Length));
                    currentPosition += alarmResults.UserData.Data.Data.Length;
                    sequenceNumber = alarmResults.UserData.Parameter.SequenceNumber;
                } while (alarmResults.UserData.Parameter.LastDataUnit == 0x01);


                alarms = S7PendingAlarmAckDatagram.TranslateFromSslData(memory, totalLength);

            }
            finally
            {
                ArrayPool<byte>.Shared.Return(memory.ToArray());
            }


            return alarms; // TODO:  change the IPlcAlarm interface!
        }

        public async Task<AlarmUpdateResult> ReceiveAlarmUpdatesAsync(CancellationToken ct)
        {
            if (ConnectionState != ConnectionState.Opened)
                throw new Dacs7NotConnectedException();

            if (!await EnableAlarmUpdatesAsync())
                throw new Dacs7NotConnectedException();

            var userId = GetNextReferenceId();
            try
            {
                var waitHandler = new CallbackHandler<S7AlarmIndicationDatagram>(userId);
                if (_alarmIndicationHandler.TryAdd(waitHandler.Id, waitHandler))
                {
                    var result = await waitHandler.Event.WaitAsync(ct);
                    if (result != null)
                    {
                        return new AlarmUpdateResult(_alarmUpdateHandler.Id == 0, result.AlarmMessage.Alarms.ToList(), () => DisableAlarmUpdatesAsync());
                    }
                }
            }
            finally
            {
                _alarmIndicationHandler.TryRemove(userId, out _);
            }

            return new AlarmUpdateResult(_alarmUpdateHandler.Id == 0, () => DisableAlarmUpdatesAsync());
        }


        private async Task<bool> EnableAlarmUpdatesAsync()
        {
            CallbackHandler<S7AlarmUpdateAckDatagram> cbh;
            if (_alarmUpdateHandler.Id == 0)
            {
                var id = GetNextReferenceId();
                var sendData = _transport.Build(S7UserDataDatagram.TranslateToMemory(S7UserDataDatagram.BuildAlarmUpdateRequest(_s7Context, id)));
                using (await SemaphoreGuard.Async(_concurrentJobs))
                {
                    if (_alarmUpdateHandler.Id == 0)
                    {
                        cbh = new CallbackHandler<S7AlarmUpdateAckDatagram>(id);
                        _alarmUpdateHandler = cbh;
                        try
                        {
                            if (await _transport.Client.SendAsync(sendData) != SocketError.Success)
                                return false;

                            await cbh.Event.WaitAsync(_s7Context.Timeout);
                        }
                        catch (Exception)
                        {
                            _alarmUpdateHandler = new CallbackHandler<S7AlarmUpdateAckDatagram>();
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        private async Task<bool> DisableAlarmUpdatesAsync()
        {
            if (_alarmUpdateHandler.Id != 0)
            {
                var sendData = _transport.Build(S7UserDataDatagram.TranslateToMemory(S7UserDataDatagram.BuildAlarmUpdateRequest(_s7Context, _alarmUpdateHandler.Id, false)));
                using (await SemaphoreGuard.Async(_concurrentJobs))
                {
                    if (_alarmUpdateHandler.Id != 0)
                    {

                        try
                        {
                            if (await _transport.Client.SendAsync(sendData) != SocketError.Success)
                                return false;

                            await _alarmUpdateHandler.Event.WaitAsync(_s7Context.Timeout);
                            _alarmUpdateHandler = new CallbackHandler<S7AlarmUpdateAckDatagram>();
                        }
                        catch (Exception)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }





        private Task ReceivedS7PendingAlarmsAckDatagram(Memory<byte> buffer)
        {
            var data = S7PendingAlarmAckDatagram.TranslateFromMemory(buffer);

            if (_alarmHandler.TryGetValue(data.UserData.Header.ProtocolDataUnitReference, out var cbh))
            {
                if (data.UserData.Data == null)
                {
                    _logger.LogWarning("No data from pending alarm  ack received for reference {0}", data.UserData.Header.ProtocolDataUnitReference);
                }
                cbh.Event.Set(data);
            }
            else
            {
                _logger.LogWarning("No read handler found for received pending alarm ack reference {0}", data.UserData.Header.ProtocolDataUnitReference);
            }

            return Task.CompletedTask;
        }

        private Task ReceivedS7AlarmUpdateAckDatagram(Memory<byte> buffer)
        {
            var data = S7AlarmUpdateAckDatagram.TranslateFromMemory(buffer);

            if (_alarmUpdateHandler.Id != 0)
            {
                if (data.UserData.Data == null)
                {
                    _logger.LogWarning("No data from alarm update ack received for reference {0}", data.UserData.Header.ProtocolDataUnitReference);
                }
                _alarmUpdateHandler.Event.Set(data);
            }
            else
            {
                _logger.LogWarning("No read handler found for received alarm update ack reference {0}", data.UserData.Header.ProtocolDataUnitReference);
            }

            return Task.CompletedTask;
        }

        private Task ReceivedS7AlarmIndicationDatagram(Memory<byte> buffer)
        {
            var data = S7AlarmIndicationDatagram.TranslateFromMemory(buffer);
            if (data.UserData.Data == null)
            {
                _logger.LogWarning("No data from alarm update ack received for reference {0}", data.UserData.Header.ProtocolDataUnitReference);
            }

            foreach (var handler in _alarmIndicationHandler.Values)
            {
                handler.Event.Set(data);
            }

            return Task.CompletedTask;
        }

    }
}