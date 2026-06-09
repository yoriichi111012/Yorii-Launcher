using System;
using System.Collections.Generic;
using System.Text;

namespace Yorii_Launcher.ViewModels
{
    public class VersionItem
    {
        public string Name { get; set; } = "";

        public bool IsSnapshot { get; set; }

        public bool IsFabric { get; set; }

        public bool IsOld { get; set; }
    }
}