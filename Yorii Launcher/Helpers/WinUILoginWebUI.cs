using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quiescent.XboxAuthNet.OAuth.CodeFlow;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;

namespace Yorii_Launcher.Helpers;

// WinUI (Microsoft.UI.Xaml WebView2) implementation of the OAuth interactive
// login surface. Replaces the legacy WinForms WebView2 broker, which cannot
// work in trimmed/NativeAOT builds (unsupported WinForms+trim combination,
// ILC turns PlatformManager.CreateWebUI into a throw-stub). The WebView2
// control itself ships inside the Windows App SDK runtime and is AOT-safe;
// this class uses no reflection.
internal sealed class WinUILoginWebUI : IWebUI
{
    private readonly WebUIOptions _options;

    // WinUI Windows are NOT rooted by showing them: if nothing references the
    // managed Window object it gets garbage-collected mid-login, the native
    // window dies underneath the auth flow and everything fails with
    // RO_E_CLOSED ("The WinUI Desktop Window object has already been
    // closed"). every login window stays in this set (UI thread only) from
    // Activate() until Closed.
    private static readonly HashSet<Window> _openWindows = [];

    public WinUILoginWebUI(WebUIOptions options) => _options = options;

    public Task<CodeFlowAuthorizationResult> DisplayDialogAndInterceptUri(
        Uri uri,
        ICodeFlowUrlChecker uriChecker,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<CodeFlowAuthorizationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // TEMP-DIAG(login-crash): per-step markers + catch. an exception inside
        // the enqueued delegate is otherwise fatal with no log. revert after.
        Logger.Info("[login-ui] DisplayDialogAndInterceptUri enter");
        try
        {
            var coreAsm = typeof(Microsoft.Web.WebView2.Core.CoreWebView2).Assembly;
            Logger.Info("[login-ui] WebView2.Core loaded as " + coreAsm.FullName + " (app dir " + System.AppContext.BaseDirectory + ")");
        }
        catch (Exception ex)
        {
            Logger.Error("[login-ui] WebView2.Core identity check failed: " + ex.GetType().Name + ": " + ex.Message);
        }
        if (!ShowOnUIThread(() =>
        {
            try
            {
                Logger.Info("[login-ui] on UI thread, creating shell");
                var (window, web) = CreateShell();
                Logger.Info("[login-ui] shell created");
                web.NavigationStarting += (_, e) =>
                {
                    var result = uriChecker.GetAuthCodeResult(new Uri(e.Uri));
                    if (result.IsEmpty)
                        return;

                    e.Cancel = true;
                    tcs.TrySetResult(result);
                    window.Close();
                };
                // user closed the window without completing login: report empty
                // (canceled), same as dismissing the legacy broker dialog.
                window.Closed += (_, _) => tcs.TrySetResult(new CodeFlowAuthorizationResult());
                Logger.Info("[login-ui] activating");
                window.Activate();
                Logger.Info("[login-ui] activated, resizing + init webview");
                window.AppWindow.Resize(new SizeInt32(520, 780));
                Initialize(web, uri, window, ex =>
                {
                    Logger.Error($"[login-ui] webview init failed: {ex}");
                    tcs.TrySetException(ex);
                });
                Logger.Info("[login-ui] init started");
            }
            catch (Exception ex)
            {
                Logger.Error($"[login-ui] shell failed: {ex}");
                tcs.TrySetException(ex);
            }
        }))
        {
            tcs.TrySetException(new InvalidOperationException(
                "Microsoft login needs the main window dispatcher, which is unavailable."));
        }

        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return tcs.Task;
    }

    public Task DisplayDialogAndNavigateUri(Uri uri, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!ShowOnUIThread(() =>
        {
            var (window, web) = CreateShell();
            window.Closed += (_, _) => tcs.TrySetResult(true);
            Initialize(web, uri, window, ex => tcs.TrySetException(ex));
            window.Activate();
        }))
        {
            tcs.TrySetException(new InvalidOperationException(
                "Microsoft sign-out needs the main window dispatcher, which is unavailable."));
        }

        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return tcs.Task;
    }

    private static bool ShowOnUIThread(DispatcherQueueHandler action)
    {
        var queue = MainWindow.Instance?.DispatcherQueue;
        if (queue is null)
            return false;

        if (queue.HasThreadAccess)
        {
            action();
            return true;
        }

        return queue.TryEnqueue(action);
    }

    private (Window Window, WebView2 Web) CreateShell()
    {
        // TEMP-DIAG(login-crash): bisect the native AV. resize moved to after
        // Activate() in the caller. revert after.
        Logger.Info("[login-ui] new Window()");
        var window = new Window();
        Logger.Info("[login-ui] new WebView2()");
        var web = new WebView2();
        Logger.Info("[login-ui] set Content");
        window.Content = web;
        window.Title = _options.Title ?? "Sign in with Microsoft";
        _openWindows.Add(window);
        window.Closed += (_, _) => _openWindows.Remove(window);
        Logger.Info("[login-ui] shell assembled");
        return (window, web);
    }

    private static async void Initialize(
        WebView2 web,
        Uri uri,
        Window window,
        Action<Exception> onError)
    {
        try
        {
            await web.EnsureCoreWebView2Async();
            Logger.Info("[login-ui] webview ready, navigating");
            web.Source = uri;
        }
        catch (Exception ex)
        {
            // TEMP-DIAG(login-crash) plan B: closing a window whose WebView2
            // failed init AVs the process (web.Close+detach+Close all died in
            // window.Close), so DON'T close - show the error in place and let
            // the user dismiss the window. revert markers after.
            Logger.Info("[login-ui] init threw, leaving shell open");
            onError(ex);
            try { Logger.Info("[login-ui] proc64=" + Environment.Is64BitProcess); } catch { }
            try
            {
                var v = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
                Logger.Info("[login-ui] in-process browser version=" + v);
            }
            catch (Exception vex)
            {
                Logger.Error("[login-ui] in-process version query failed: " + vex.GetType().Name + ": " + vex.Message);
            }
            // (registry/LoadLibrary probe removed: all three succeeded
            // in-process - pv=152.0.4191.66, dll exists, LoadLibrary ok.
            // the break is the staged file itself, see below)
            // (probe1/probe2 removed: this projection exposes no 2/3-arg
            // CreateAsync; mechanism already proven by file forensics below)
            try
            {
                window.Content = new TextBlock
                {
                    Text = "Microsoft sign-in could not start: the WebView2 browser engine failed to load (0x800700C1). Close this window - the login will report as failed.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(24),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
            }
            catch { }
            Logger.Info("[login-ui] shell left open with error");
        }
    }
}
