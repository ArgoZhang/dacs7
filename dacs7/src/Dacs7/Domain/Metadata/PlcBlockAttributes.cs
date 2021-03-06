﻿// Copyright (c) Benjamin Proemmer. All rights reserved.
// See License in the project root for license information.

using System;

namespace Dacs7.Metadata
{
    [Flags]
    public enum PlcBlockAttributes : byte
    {
        None = 0,
        Linked = 1,
        StandardBlock = 2,
        KnowHowProtected = 4,
        NotRetain = 6
    }
}
