﻿using Dacs7;
using Dacs7Cli.Options;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Dacs7Cli
{
    internal static class ReadAlarmsCommand
    {
        internal static void Register(CommandLineApplication app)
        {
            app.Command("readalarms", cmd =>
            {
                cmd.Description = "Read alarms from the plc.";

                var addressOption = cmd.Option("-a | --address", "The IPAddress of the plc", CommandOptionType.SingleValue);
                var debugOption = cmd.Option("-d | --debug", "Activate debug output", CommandOptionType.NoValue);
                var traceOption = cmd.Option("-t | --trace", "Trace also dacs7 internals", CommandOptionType.NoValue);
                var maxJobsOption = cmd.Option("-j | --jobs", "Maximum number of concurrent jobs.", CommandOptionType.SingleValue);

                cmd.OnExecute(async () =>
                {
                    ReadAlarmsOptions readOptions = null; ;
                    try
                    {
                        readOptions = new ReadAlarmsOptions
                        {
                            Debug = debugOption.HasValue(),
                            Trace = traceOption.HasValue(),
                            Address = addressOption.HasValue() ? addressOption.Value() : "localhost",
                            MaxJobs = maxJobsOption.HasValue() ? Int32.Parse(maxJobsOption.Value()) : 10,
                        }.Configure();
                        var result = await ReadAlarms(readOptions, readOptions.LoggerFactory);

                        await Task.Delay(500);

                        return result;
                    }
                    finally
                    {
                        readOptions?.LoggerFactory?.Dispose();
                    }
                });
            });
        }



        private static async Task<int> ReadAlarms(ReadAlarmsOptions readOptions, ILoggerFactory loggerFactory)
        {
            var client = new Dacs7Client(readOptions.Address, PlcConnectionType.Pg, 5000, loggerFactory)
            {
                MaxAmQCalled = (ushort)readOptions.MaxJobs,
                MaxAmQCalling = (ushort)readOptions.MaxJobs
            };
            var logger = loggerFactory?.CreateLogger("Dacs7Cli.ReadAlarms");

            try
            {
                long msTotal = 0;
                await client.ConnectAsync();

                try
                {
                    var sw = new Stopwatch();
                    sw.Start();
                    var results = await client.ReadPendingAlarmsAsync();
                    foreach (var alarm in results)
                    {
                        Console.WriteLine($"Alarm update: ID: {alarm.Id}   MsgNumber: {alarm.MsgNumber}  IsAck: {alarm.IsAck} ", alarm);
                    }
                    sw.Stop();
                    msTotal += sw.ElapsedMilliseconds;
                    logger?.LogDebug($"ReadAlarmsTime: {sw.Elapsed}");

                }
                catch (Exception ex)
                {
                    logger?.LogError($"Exception in read alarms {ex.Message}.");
                }

            }
            catch (Exception ex)
            {
                logger?.LogError($"An error occured in ReadAlarms: {ex.Message} - {ex.InnerException?.Message}");
                return 1;
            }
            finally
            {
                await client.DisconnectAsync();
            }

            return 0;
        }

    }
}
