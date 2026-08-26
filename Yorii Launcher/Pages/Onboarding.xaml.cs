using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Yorii_Launcher.Helpers;

// to learn more about winui, the winui project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info

namespace Yorii_Launcher.Pages;

/// <summary>
/// an empty page that can be used on its own or navigated to within a frame
/// </summary>
public sealed partial class Onboarding : Page
{
    public Onboarding()
    {
        InitializeComponent();
        MemoryOptimizer.ReduceMemory();
    }
}