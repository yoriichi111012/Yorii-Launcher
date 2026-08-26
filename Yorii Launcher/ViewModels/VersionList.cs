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

        public bool IsForge { get; set; }

        public bool IsNeoForge { get; set; }

        // public bool isoptifine { get; set; }

        public bool IsOld { get; set; }

        // true when this exact version name exists in the versions folder
        // which pins it to the top of the list
        public bool IsInstalled { get; set; }
    }
}