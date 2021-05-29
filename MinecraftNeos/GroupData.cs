using System;
using System.Collections.Generic;
using System.Text;
using BaseX;

namespace MinecraftNeos
{
    public class GroupData
    {
        public readonly string file;
        public readonly List<int3> lightSources = new List<int3>();

        public GroupData(string file)
        {
            this.file = file;
        }
    }
}
