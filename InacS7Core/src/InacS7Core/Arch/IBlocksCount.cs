﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InacS7Core.Arch
{
    public interface IPlcBlocksCount
    {
        int Ob { get; }
        int Fb { get;}
        int Fc { get; }
        int Sfb { get; }
        int Sfc { get;  }
        int Db { get;}
        int Sdb { get; }
    }
}
