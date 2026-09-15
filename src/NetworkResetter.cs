using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

[assembly: AssemblyTitle("Network Resetter")]
[assembly: AssemblyDescription("All-in-one Windows network repair utility")]
[assembly: AssemblyProduct("Network Resetter")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

namespace NetworkResetter
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                using (var mutex = new Mutex(false, @"Global\NetworkResetter.Aio.v2"))
                {
                    bool owns;
                    try { owns = mutex.WaitOne(0); }
                    catch (AbandonedMutexException) { owns = true; }
                    if (!owns)
                    {
                        MessageBox.Show("Network Resetter is already open.", "Network Resetter");
                        return 1;
                    }
                    try
                    {
                        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                        {
                            MessageBox.Show("Administrator access is required.", "Administrator access needed");
                            return 1;
                        }
                        var app = new Application();
                        var controller = new Controller();
                        return app.Run(controller.Window);
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("The app could not continue.\n\n" + ex.Message, "Network Resetter", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }

    internal sealed class Step : INotifyPropertyChanged
    {
        public int Number { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public List<Command> Commands = new List<Command>();
        private string state = "Waiting", color = "#75839B";
        public string State { get { return state; } }
        public string Color { get { return color; } }
        public event PropertyChangedEventHandler PropertyChanged;
        public void SetState(string value, string foreground)
        {
            state = value; color = foreground;
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs("State"));
                PropertyChanged(this, new PropertyChangedEventArgs("Color"));
            }
        }
    }

    internal sealed class Command
    {
        public string File, Arguments;
        public bool NeedsRestart;
        public Command(string file, string arguments, bool needsRestart)
        { File = file; Arguments = arguments; NeedsRestart = needsRestart; }
    }

    internal sealed class CommandResult
    {
        public int ExitCode;
        public string Output;
    }

    internal sealed class Controller
    {
        public Window Window { get; private set; }
        private readonly ObservableCollection<Step> steps = new ObservableCollection<Step>();
        private readonly object logLock = new object();
        private StreamWriter logWriter;
        private string sessionDirectory;
        private volatile bool stopRequested, loggingFailed;
        private bool busy, mutationStarted, restartNeeded, sessionAttempted;
        private RadioButton basic;
        private ComboBox connections;
        private bool connectionChecked;
        private string repairAdapterId, connectionNotes = "No connection check recorded.";
        private CheckBox renew, acknowledge;
        private Button run, restart;
        private TextBox activity;

        public Controller()
        {
            using (Stream stream = Resource("MainWindow.xaml")) Window = (Window)XamlReader.Load(stream);
            // Startup only creates the UI; no network inspection or modification.
            Rect work = SystemParameters.WorkArea;
            Window.MinWidth = Math.Min(Window.MinWidth, work.Width);
            Window.MinHeight = Math.Min(Window.MinHeight, work.Height);
            Window.Width = Math.Min(Window.Width, work.Width);
            Window.Height = Math.Min(Window.Height, work.Height);
            using (Stream stream = Resource("Logo.png"))
            {
                var logo = new BitmapImage();
                logo.BeginInit(); logo.CacheOption = BitmapCacheOption.OnLoad;
                logo.StreamSource = stream; logo.EndInit(); logo.Freeze();
                Find<Image>("BrandLogo").Source = logo; Window.Icon = logo;
            }
            basic = Find<RadioButton>("BasicMode");
            connections = Find<ComboBox>("Connections");
            renew = Find<CheckBox>("RenewDhcp"); acknowledge = Find<CheckBox>("Acknowledge");
            run = Find<Button>("RunButton"); restart = Find<Button>("RestartButton");
            activity = Find<TextBox>("ActivityLog");
            Find<ItemsControl>("StepList").ItemsSource = steps;
            basic.Checked += delegate { SelectMode(); };
            Find<RadioButton>("StackMode").Checked += delegate { SelectMode(); };
            Find<Expander>("AdvancedOptions").Collapsed += delegate { if (!busy && !sessionAttempted) basic.IsChecked = true; };
            connections.SelectionChanged += delegate { ConnectionSelected(); };
            Find<Button>("CheckButton").Click += async delegate { await CheckConnection(); };
            Find<Button>("NewSession").Click += delegate { NewSession(); };
            Find<RadioButton>("AggressiveMode").Checked += delegate { SelectMode(); };
            renew.Checked += delegate { SelectMode(); };
            renew.Unchecked += delegate { SelectMode(); };
            acknowledge.Checked += delegate { UpdateRunButton(); };
            acknowledge.Unchecked += delegate { UpdateRunButton(); };
            run.Click += async delegate { await RunClicked(); };
            restart.Click += async delegate { await RestartClicked(); };
            Find<Button>("OpenLogs").Click += delegate { OpenSession(); };
            Find<Button>("HelpButton").Click += delegate { ShowHelp(); };
            Window.Closing += delegate(object sender, CancelEventArgs e)
            {
                if (busy)
                {
                    e.Cancel = true;
                    MessageBox.Show(Window, "Wait for the current operation to finish. Stop skips the remaining repair steps.", "Still running", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            };
            SelectMode();
            AppendLog("Ready. Select Check to inspect the connection.");
        }

        private static Stream Resource(string name)
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream == null) throw new InvalidOperationException("Resource not found: " + name);
            return stream;
        }
        private T Find<T>(string name) where T : class
        {
            T element = Window.FindName(name) as T;
            if (element == null) throw new InvalidOperationException("Control not found: " + name);
            return element;
        }

        private string ModeName
        {
            get
            {
                bool quick = basic.IsChecked == true;
                bool stack = Find<RadioButton>("StackMode").IsChecked == true;
                bool adapters = Find<RadioButton>("AggressiveMode").IsChecked == true;
                if ((quick ? 1 : 0) + (stack ? 1 : 0) + (adapters ? 1 : 0) != 1) return null;
                return quick ? "Quick repair" : stack ? "Winsock reset" : "Full adapter reset";
            }
        }

        private void SelectMode()
        {
            if (busy || sessionAttempted) return;
            string selectedMode = ModeName;
            Find<TextBlock>("SelectedModeText").Text = selectedMode == null ? "Select a repair" : "Selected: " + selectedMode;
            if (selectedMode == null) { steps.Clear(); run.IsEnabled = false; return; }
            bool quick = selectedMode == "Quick repair";
            AdapterInfo selected = connections.SelectedItem as AdapterInfo;
            renew.Visibility = quick ? Visibility.Visible : Visibility.Collapsed;
            renew.IsEnabled = selected != null && selected.Connected && selected.Dhcp == true;
            acknowledge.Visibility = quick ? Visibility.Collapsed : Visibility.Visible;
            acknowledge.IsChecked = false;
            Find<Border>("ImpactPanel").Background = new SolidColorBrush(quick ? Color.FromRgb(237, 241, 247) : Color.FromRgb(255, 245, 231));
            Find<Border>("ImpactPanel").BorderBrush = new SolidColorBrush(quick ? Color.FromRgb(224, 230, 240) : Color.FromRgb(244, 222, 192));
            Find<TextBlock>("ImpactText").Text = quick
                ? "Manual IP/DNS settings are kept. IP renewal may briefly interrupt the connection."
                : ModeName == "Winsock reset"
                    ? "Resets network sockets for all connections. Restart required."
                    : "Removes all adapters. VPNs and virtual networks may need setup again. Restart required.";
            BuildSteps(ModeName, quick && renew.IsEnabled && renew.IsChecked == true);
            UpdateRunButton();
        }
        private void BuildSteps(string mode, bool includeDhcp)
        {
            steps.Clear();
            AddStep("Save current settings", "Save IP and adapter settings.");
            if (mode == "Quick repair")
            {
                AddStep("Clear DNS cache", "Remove cached website addresses.", new Command("ipconfig.exe", "/flushdns", false));
                if (includeDhcp)
                    AddStep("Renew IP address", "Request an IPv4 lease for this adapter.", new Command("ipconfig.exe", "/renew", false));
            }
            else if (mode == "Winsock reset")
            {
                AddStep("Reset Winsock", "Reset Windows network sockets.", new Command("netsh.exe", "winsock reset", true));
            }
            else if (mode == "Full adapter reset") AddStep("Reset all network adapters", "Remove network devices. Restart afterward.", new Command("netcfg.exe", "-d", true));
            else throw new InvalidOperationException("No valid repair mode selected.");
            Find<TextBlock>("StepCount").Text = steps.Count + " steps";
        }
        private void AddStep(string title, string detail, params Command[] commands)
        {
            steps.Add(new Step { Number = steps.Count + 1, Title = title, Detail = detail, Commands = new List<Command>(commands) });
        }
        private void UpdateRunButton()
        {
            if (busy) return;
            if (sessionAttempted)
            {
                run.IsEnabled = true;
                run.Content = "Check again";
                return;
            }
            AdapterInfo selected = connections.SelectedItem as AdapterInfo;
            bool allowed = basic.IsChecked == true ? selected != null && selected.Connected : acknowledge.IsChecked == true;
            run.IsEnabled = !sessionAttempted && connectionChecked && allowed && ModeName != null;
            if (!sessionAttempted) run.Content = ModeName == "Quick repair" ? "Quick repair" : ModeName == "Winsock reset" ? "Reset Winsock..." : "Reset all adapters...";
        }
        private void SetEditorEnabled(bool enabled)
        {
            Find<Grid>("ModeSelector").IsEnabled = enabled;
            connections.IsEnabled = enabled;
            renew.IsEnabled = enabled && connections.SelectedItem is AdapterInfo && ((AdapterInfo)connections.SelectedItem).Dhcp == true && ((AdapterInfo)connections.SelectedItem).Connected;
            acknowledge.IsEnabled = enabled;
        }
        private void ConnectionSelected()
        {
            AdapterInfo selected = connections.SelectedItem as AdapterInfo;
            Find<TextBlock>("ConnectionSummary").Text = selected == null ? "No network adapter found." : selected.Summary;
            Find<TextBlock>("ConnectionAdvice").Text = selected == null ? "Check the adapter in Windows Device Manager." : selected.Advice;
            connectionNotes = Find<TextBlock>("ConnectionSummary").Text + "\n" + Find<TextBlock>("ConnectionAdvice").Text + "\n" + Find<TextBlock>("DnsResult").Text;
            if (!busy && !sessionAttempted)
            {
                renew.IsChecked = selected != null && selected.Dhcp == true && selected.Connected;
                SelectMode();
            }
        }
        private async Task CheckConnection()
        {
            if (busy) return;
            busy = true;
            run.IsEnabled = false; restart.IsEnabled = false;
            Find<Button>("CheckButton").IsEnabled = false;
            Find<Button>("NewSession").IsEnabled = false;
            SetEditorEnabled(false);
            string previousId = connections.SelectedItem is AdapterInfo ? ((AdapterInfo)connections.SelectedItem).Id : null;
            Find<TextBlock>("ConnectionSummary").Text = "Checking IP and DNS...";
            Find<TextBlock>("ConnectionAdvice").Text = "Checking...";
            Find<Button>("CheckButton").Content = "Checking...";
            try
            {
                CheckResult result = await NetworkChecks.CheckAsync();
                connections.ItemsSource = result.Adapters;
                AdapterInfo previous = result.Adapters.Find(a => a.Id == previousId);
                connections.SelectedItem = previous ?? result.Adapters.Find(a => a.Connected) ?? (result.Adapters.Count > 0 ? result.Adapters[0] : null);
                connectionChecked = true;
                ConnectionSelected();
                Find<TextBlock>("DnsResult").Text = result.DnsResult + "\nLookup uses Windows routing; it may use another adapter or VPN.";
                connectionNotes = Find<TextBlock>("ConnectionSummary").Text + "\n" + Find<TextBlock>("ConnectionAdvice").Text + "\n" + Find<TextBlock>("DnsResult").Text;
                AppendLog("CONNECTION CHECK\n" + connectionNotes);
                SetStatus("Check complete", restartNeeded ? "Restart to finish the reset." : "Try a website. If it still fails, use Quick repair.");
                Find<TextBlock>("ConnectionAdvice").BringIntoView();
            }
            catch (Exception ex)
            {
                connectionChecked = false;
                Find<TextBlock>("ConnectionSummary").Text = "Check failed: " + ex.Message;
                Find<TextBlock>("ConnectionAdvice").Text = "Check failed. Try again or open Connection details.";
                Find<TextBlock>("DnsResult").Text = "DNS lookup not completed.";
                AppendLog("Connection check failed: " + ex.Message);
            }
            finally
            {
                busy = false;
                Find<Button>("CheckButton").IsEnabled = true;
                Find<Button>("CheckButton").Content = "Check again";
                Find<Button>("NewSession").IsEnabled = true;
                restart.IsEnabled = true;
                SetEditorEnabled(!sessionAttempted);
                if (!sessionAttempted && connectionChecked) ConnectionSelected();
                UpdateRunButton();
            }
        }
        private void NewSession()
        {
            if (busy || restartNeeded) return;
            sessionAttempted = false; mutationStarted = false;
            Find<Border>("ResultPanel").Visibility = Visibility.Collapsed;
            Find<Button>("NewSession").Visibility = Visibility.Collapsed;
            Find<ProgressBar>("Progress").Value = 0;
            Find<TextBlock>("ProgressLabel").Text = "Ready";
            SetEditorEnabled(true);
            SelectMode();
            SetStatus("Select a repair", "Select Quick repair or an advanced reset.");
        }

        private async Task RunClicked()
        {
            if (busy)
            {
                stopRequested = true; run.IsEnabled = false; run.Content = "Waiting...";
                AppendLog("Stop requested. Waiting for the current step.");
                return;
            }
            if (sessionAttempted) { await CheckConnection(); return; }
            if (basic.IsChecked != true && acknowledge.IsChecked != true) return;
            if (!connectionChecked) return;
            string mode = ModeName;
            if (mode == null) return;
            AdapterInfo selected = connections.SelectedItem as AdapterInfo;
            if (mode == "Quick repair" && (selected == null || !selected.Connected)) return;
            repairAdapterId = selected == null ? null : selected.Id;
            bool includeDhcp = mode == "Quick repair" && renew.IsEnabled && renew.IsChecked == true;
            // Build the executable plan from the exact mode being confirmed, not a cached preview.
            BuildSteps(mode, includeDhcp);
            var confirmedPlan = new List<Step>(steps);
            string message = mode == "Quick repair"
                ? "Remove cached website addresses."
                : mode == "Winsock reset"
                    ? "Reset Windows network sockets for all connections. Restart required."
                    : "Remove all network adapters. VPNs and virtual networks may need setup again. Restart required.";
            if (includeDhcp)
                message += "\n\nRenew automatic IPv4 for: " + selected.Name + ". The connection may be interrupted. An offline DHCP server still needs attention.";
            message += "\n\nSave online work. Use this PC locally; remote access may disconnect. Settings will be recorded, but there is no automatic undo.";
            if (!Confirm(mode + "?", message, mode == "Quick repair" ? "Repair" : "Reset")) return;
            if (ModeName != mode) return;
            busy = true; sessionAttempted = true; stopRequested = false; loggingFailed = false;
            SetEditorEnabled(false);
            Find<Button>("CheckButton").IsEnabled = false;
            Find<Button>("NewSession").Visibility = Visibility.Collapsed;
            run.Content = "Stop"; run.IsEnabled = true;
            Find<ProgressBar>("Progress").IsIndeterminate = false;
            Find<TextBlock>("ProgressLabel").Text = "Running";
            Find<Expander>("StepsExpander").IsExpanded = true;
            Find<Expander>("StepsExpander").BringIntoView();
            SetStatus("Preparing repair", "Saving current settings.");

            try
            {
                await Task.Run(delegate { ExecutePlan(mode, confirmedPlan); });
                bool stopped = stopRequested && HasSkippedSteps();
                if (stopped)
                    SetStatus("Stopped", mutationStarted ? "Some changes were applied. Review the activity log." : "No settings were changed.");
                else SetStatus("Repair finished", restartNeeded ? "Restart, then check the connection." : "Select Check again, then try a website to confirm access.");
                Find<TextBlock>("ProgressLabel").Text = stopped ? "Stopped" : "Done";
            }
            catch (OperationCanceledException)
            {
                SetStatus("Stopped", "No settings were changed.");
                Find<TextBlock>("ProgressLabel").Text = "Stopped";
                foreach (Step step in steps) if (step.State == "Running") step.SetState("Stopped", "#75839B");
                SkipWaitingSteps();
            }
            catch (Exception ex)
            {
                AppendLog("ERROR: " + ex.Message);
                SetStatus("Repair incomplete", mutationStarted ? "A step failed. Some changes may be applied. See the activity log." : "Preparation failed. No changes made. See the activity log.");
                Find<TextBlock>("ProgressLabel").Text = "Failed";
                Find<Expander>("LogExpander").IsExpanded = true;
                foreach (Step step in steps) if (step.State == "Running") step.SetState("Failed", "#B33E36");
                SkipWaitingSteps();
            }
            finally
            {
                busy = false;
                Find<Button>("CheckButton").IsEnabled = true;
                Find<Button>("CheckButton").Content = "Check again";
                Find<Button>("NewSession").Visibility = restartNeeded ? Visibility.Collapsed : Visibility.Visible;
                ShowNextSteps();
                Find<ProgressBar>("Progress").IsIndeterminate = false;
                run.Content = "Check again"; run.IsEnabled = true;
                restart.Visibility = restartNeeded ? Visibility.Visible : Visibility.Collapsed;
                AppendLog("Session ended. " + Find<TextBlock>("StatusTitle").Text);
                lock (logLock)
                {
                    if (logWriter != null)
                    {
                        try { logWriter.Dispose(); }
                        catch (IOException) { loggingFailed = true; }
                        logWriter = null;
                    }
                }
                if (loggingFailed)
                {
                    Find<Expander>("LogExpander").IsExpanded = true;
                    SetStatus("Log not saved", "Copy the activity log from this window. Some changes may be applied.");
                    Find<TextBlock>("ProgressLabel").Text = "Failed";
                }
                Find<Border>("ResultPanel").BringIntoView();
            }
        }

        private bool HasSkippedSteps()
        {
            foreach (Step step in steps) if (step.State == "Skipped") return true;
            return false;
        }
        private void SkipWaitingSteps()
        {
            foreach (Step step in steps) if (step.State == "Waiting") step.SetState("Skipped", "#75839B");
        }

        private void ExecutePlan(string mode, IList<Step> plan)
        {
            // Fail closed before network operations if a command does not match the confirmed mode.
            foreach (Step item in plan)
                foreach (Command command in item.Commands)
                {
                    bool allowed = mode == "Quick repair"
                        ? command.File == "ipconfig.exe" && (command.Arguments == "/flushdns" || command.Arguments == "/renew")
                        : mode == "Winsock reset"
                            ? command.File == "netsh.exe" && command.Arguments == "winsock reset"
                            : mode == "Full adapter reset" && command.File == "netcfg.exe" && command.Arguments == "-d";
                    if (!allowed) throw new InvalidOperationException("The command list does not match the confirmed mode. No repair was started.");
                }
            sessionDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetworkResetter", "Sessions", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(sessionDirectory);
            lock (logLock) logWriter = new StreamWriter(Path.Combine(sessionDirectory, "activity.log"), false, new UTF8Encoding(true)) { AutoFlush = true };
            UI(delegate
            {
                Find<TextBlock>("SessionPath").Text = "Session folder: " + sessionDirectory;
                Find<Button>("OpenLogs").IsEnabled = true;
                plan[0].SetState("Running", "#1734BB");
            });
            AppendLog("Network Resetter 1.0 | Mode " + mode + " | " + DateTime.Now.ToString("O"));
            foreach (Step item in plan)
                foreach (Command command in item.Commands) AppendLog("Planned: " + command.File + " " + command.Arguments);
            AppendLog(connectionNotes);
            File.WriteAllText(Path.Combine(sessionDirectory, "README.txt"),
                "NETWORK SETTINGS BEFORE REPAIR\r\n\r\n" +
                "ipconfig-before.txt: adapter and IP configuration before repair.\r\n" +
                "interface-before.txt: netsh interface dump output.\r\n" +
                "activity.log: commands, output, errors, and exit codes.\r\n" +
                "This is NOT a full backup. Drivers, VPN clients, virtual switches, and all application settings are not backed up.\r\n" +
                "Use these records as a reference for restoring custom IP/DNS settings or asking for help. Review dumps before using them.\r\n" +
                "Exit code 0 means Windows reported command completion, not proof of internet access. Review output for partial failures.\r\n" +
                "A restart may still be needed after a partially failed reset.\r\n", new UTF8Encoding(true));
            ThrowIfStopped();
            SaveSnapshot("ipconfig.exe", "/all", "ipconfig-before.txt");
            ThrowIfStopped();
            SaveSnapshot("netsh.exe", "interface dump", "interface-before.txt");
            EnsureLogging();
            UI(delegate { plan[0].SetState("Saved", "#247558"); Find<ProgressBar>("Progress").Value = 100.0 / plan.Count; });
            for (int index = 1; index < plan.Count; index++)
            {
                if (stopRequested) { UI(SkipWaitingSteps); break; }
                EnsureLogging();
                Step step = plan[index];
                UI(delegate
                {
                    step.SetState("Running", "#1734BB");
                    SetStatus(step.Title, "Step " + step.Number + " of " + plan.Count + ". Please wait for the current operation.");
                });
                var failures = new List<string>();
                // Renew without first releasing the address. Revalidate the selected adapter at execution time.
                foreach (Command command in step.Commands)
                {
                    try
                    {
                        string arguments = command.Arguments;
                        if (command.File == "ipconfig.exe" && arguments == "/renew")
                        {
                            AdapterInfo target = NetworkChecks.ValidateDhcpTarget(repairAdapterId);
                            arguments = "/renew \"" + target.Name + "\"";
                        }
                        mutationStarted = true;
                        if (command.NeedsRestart) restartNeeded = true;
                        CommandResult result = RunCommand(command.File, arguments);
                        if (result.ExitCode != 0)
                            failures.Add(command.File + " " + command.Arguments + " (exit code " + result.ExitCode + ")");
                    }
                    catch (Exception ex) { failures.Add(command.File + ": " + ex.Message); }
                }
                if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
                EnsureLogging();
                int completed = index + 1;
                UI(delegate { step.SetState("Completed", "#247558"); Find<ProgressBar>("Progress").Value = completed * 100.0 / plan.Count; });
            }
        }

        private void ThrowIfStopped() { if (stopRequested) throw new OperationCanceledException(); }
        private void SaveSnapshot(string file, string arguments, string destination)
        {
            CommandResult result = RunCommand(file, arguments, false);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
                throw new InvalidOperationException("Could not save the original settings: " + file + " " + arguments + " (code " + result.ExitCode + "). Repair canceled.");
            File.WriteAllText(Path.Combine(sessionDirectory, destination), result.Output, new UTF8Encoding(true));
            AppendLog("Saved settings: " + destination);
        }

        private CommandResult RunCommand(string file, string arguments, bool showOutput = true)
        {
            // Fixed utility paths. DHCP names are validated and quoted; no shell or external scripts.
            string executable = Path.Combine(Environment.SystemDirectory, file);
            if (!File.Exists(executable)) throw new FileNotFoundException("Windows utility not found.", executable);
            AppendLog("> " + file + " " + arguments);
            var output = new StringBuilder();
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(executable, arguments)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                    StandardErrorEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                    WorkingDirectory = sessionDirectory ?? Environment.SystemDirectory
                };
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (output) output.AppendLine(e.Data);
                    AppendLog(e.Data, showOutput);
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    AppendLog("[stderr] " + e.Data);
                };
                process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                int seconds = 0;
                while (!process.WaitForExit(1000))
                {
                    seconds++;
                    if (seconds % 60 == 0)
                    {
                        AppendLog("Still waiting for " + file + " (" + seconds + " seconds). The active command will not be forcibly stopped.");
                        PostUI(delegate { Find<TextBlock>("StatusDetail").Text = "Still waiting for Windows. You can stop after this step."; });
                    }
                }
                // Drain both asynchronous output streams before disposing or advancing.
                process.WaitForExit();
                int code = process.ExitCode;
                AppendLog("Exit code: " + code);
                return new CommandResult { ExitCode = code, Output = output.ToString() };
            }
        }

        private void AppendLog(string line, bool showInUi = true)
        {
            string text = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line;
            lock (logLock)
            {
                if (logWriter != null && !loggingFailed)
                {
                    try { logWriter.WriteLine(text); }
                    catch (Exception ex)
                    {
                        if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                        loggingFailed = true;
                        showInUi = true;
                        text += "\r\n[ERROR] Could not write the activity log: " + ex.Message;
                    }
                }
            }
            if (showInUi && (!string.IsNullOrWhiteSpace(line) || loggingFailed))
                PostUI(delegate { activity.AppendText(text + Environment.NewLine); activity.ScrollToEnd(); });
        }
        private void EnsureLogging() { if (loggingFailed) throw new IOException("Writing the activity log failed. No further steps will run."); }
        private void UI(Action action) { Window.Dispatcher.Invoke(action); }
        private void PostUI(Action action) { if (!Window.Dispatcher.HasShutdownStarted) Window.Dispatcher.BeginInvoke(action); }
        private void SetStatus(string title, string detail)
        {
            Find<TextBlock>("StatusTitle").Text = title; Find<TextBlock>("StatusDetail").Text = detail;
        }

        private bool Confirm(string title, string text, string action)
        {
            var dialog = new Window
            {
                Owner = Window, Title = title, Width = Math.Min(490, SystemParameters.WorkArea.Width), SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false, Background = Brushes.White, FontFamily = Window.FontFamily,
                Foreground = Window.Foreground, Icon = Window.Icon, MaxHeight = SystemParameters.WorkArea.Height
            };
            var panel = new StackPanel { Margin = new Thickness(26) };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 21, Margin = new Thickness(0, 16, 0, 24), Foreground = new SolidColorBrush(Color.FromRgb(87, 102, 128)) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "Cancel", IsCancel = true, IsDefault = true, Style = (Style)Window.FindResource(typeof(Button)), Margin = new Thickness(0, 0, 10, 0) };
            var accept = new Button { Content = action, Style = (Style)Window.FindResource("PrimaryButton") };
            cancel.Click += delegate { dialog.DialogResult = false; };
            accept.Click += delegate { dialog.DialogResult = true; };
            buttons.Children.Add(cancel); buttons.Children.Add(accept); panel.Children.Add(buttons);
            dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            dialog.Loaded += delegate { cancel.Focus(); };
            return dialog.ShowDialog() == true;
        }

        private async Task RestartClicked()
        {
            if (busy || !restartNeeded) return;
            if (!Confirm("Restart this computer?", "Save your work before restarting.\n\nOpen apps may delay or cancel the restart. They will not be forcibly closed.", "Restart now")) return;
            busy = true; restart.IsEnabled = false; run.IsEnabled = false;
            Find<Button>("CheckButton").IsEnabled = false;
            try
            {
                CommandResult result = await Task.Run(delegate { return RunCommand("shutdown.exe", "/r /t 0"); });
                if (result.ExitCode != 0) throw new InvalidOperationException("Windows rejected the restart request (code " + result.ExitCode + ").");
                SetStatus("Restart requested", "If needed, restart from the Windows Start menu.");
            }
            catch (Exception ex)
            {
                AppendLog("Restart failed: " + ex.Message);
                SetStatus("Restart failed", "Save your work and restart from the Start menu.");
            }
            finally { busy = false; restart.IsEnabled = true; Find<Button>("CheckButton").IsEnabled = true; }
        }
        private void OpenSession()
        {
            try
            {
                if (!string.IsNullOrEmpty(sessionDirectory) && Directory.Exists(sessionDirectory))
                    Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + sessionDirectory + "\"") { UseShellExecute = false });
            }
            catch (Exception ex) { MessageBox.Show(Window, "Log folder: " + sessionDirectory + "\n\n" + ex.Message, "Open logs"); }
        }
        private void ShowNextSteps()
        {
            Find<Border>("ResultPanel").Visibility = Visibility.Visible;
            Find<TextBlock>("NextSteps").Text = restartNeeded
                ? "Restart Windows, then check the connection.\nRestore custom IP/DNS or VPN settings if needed."
                : "Check again, then try a website.\nStill offline? Check Wi-Fi/cable and your router, or contact IT.\nIf only this PC fails, try Other repairs.";
        }
        private void ShowHelp()
        {
            MessageBox.Show(Window,
                "CHECK\nReads IP and DNS settings and looks up www.microsoft.com. Settings are not changed. A lookup result does not confirm internet access.\n\n" +
                "QUICK REPAIR\nClears the DNS cache. Renew automatic IP requests an IPv4 DHCP lease for the selected adapter. Manual IP/DNS settings are kept.\n\n" +
                "ADVANCED REPAIRS\nWinsock reset resets Windows network sockets. Full adapter reset removes network devices. Both require a restart.\n\n" +
                "STILL OFFLINE?\nCheck Wi-Fi/cable and the router. If other devices also fail, contact IT or your provider. This app cannot repair an offline server.\n\n" +
                "LOGS\nOpen logs contains saved settings and command output. These records are not a full backup or automatic undo.\n\n" +
                "STOP\nWaits for the current step to finish, then skips the rest. Restart requires separate confirmation.",
                "Help", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
