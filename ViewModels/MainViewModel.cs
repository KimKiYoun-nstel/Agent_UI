using System;
using System.Collections.ObjectModel;
using Microsoft.Win32;
using System.Windows.Forms;
using Agent.UI.Wpf.Services;

namespace Agent.UI.Wpf.ViewModels
{
    public sealed class MainViewModel : ObservableObject, IAsyncDisposable
    {
        // Agent client (transport+codec)
        private readonly AgentClient _agent;

        // Connection
        public string[] Roles { get; } = new[] { "client", "server" };
        private string _selectedRole = "client";
        public string SelectedRole { get => _selectedRole; set => SetField(ref _selectedRole, value); }

        private string _address = "127.0.0.1";
        public string Address { get => _address; set => SetField(ref _address, value); }

    private string _port = "25000";
        public string Port { get => _port; set => SetField(ref _port, value); }

        // Config
        private string _configRoot;
        public string ConfigRoot 
        { 
            get => _configRoot; 
            set => SetField(ref _configRoot, value); 
        }

        // DDS
        private string _domainId = "0";
        public string DomainId { get => _domainId; set => SetField(ref _domainId, value); }

        public ObservableCollection<string> QosProfiles { get; } = new();
        private string? _selectedQosProfile;
        public string? SelectedQosProfile { get => _selectedQosProfile; set => SetField(ref _selectedQosProfile, value); }

        public ObservableCollection<string> TypeNames { get; } = new();
        private string? _selectedType;
        public string? SelectedType
        {
            get => _selectedType;
            set
            {
                if (SetField(ref _selectedType, value))
                {
                    // Whenever the selected type changes, update Topic default (strip C_)
                    if (!string.IsNullOrWhiteSpace(_selectedType))
                    {
                        var baseTopic = _selectedType.StartsWith("C_") ? _selectedType.Substring(2) : _selectedType;
                        Topic = baseTopic;
                    }
                }
            }
        }

        private string _topic = "";
        public string Topic { get => _topic; set => SetField(ref _topic, value); }

        private string _payload = "{}";
        public string Payload { get => _payload; set => SetField(ref _payload, value); }

    // --- Pub/Sub names ---
    private string _publisherName = "pub1";
    public string PublisherName { get => _publisherName; set => SetField(ref _publisherName, value); }

    private string _subscriberName = "sub1";
    public string SubscriberName { get => _subscriberName; set => SetField(ref _subscriberName, value); }

        // Log
        public string[] LogLevels { get; } = new[] { "Info", "Debug" };
        private string _selectedLogLevel = "Info";
        public string SelectedLogLevel { get => _selectedLogLevel; set => SetField(ref _selectedLogLevel, value); }
        public ObservableCollection<string> Logs { get; } = new();

        private string _status = "Ready";
        public string Status { get => _status; set => SetField(ref _status, value); }

        // Commands
        public RelayCommand ConnectCommand { get; }
        public RelayCommand DisconnectCommand { get; }
        public RelayCommand BrowseConfigCommand { get; }
        public RelayCommand ReloadConfigCommand { get; }
        public RelayCommand CreateParticipantCommand { get; }
        public RelayCommand ClearDdsCommand { get; }
        public RelayCommand OpenFormCommand { get; }
        public RelayCommand FillSampleCommand { get; }
        public RelayCommand CreateWriterCommand { get; }
        public RelayCommand CreateReaderCommand { get; }
        public RelayCommand PublishCommand { get; }
        public RelayCommand ClearLogCommand { get; }
    public RelayCommand CopyLogCommand { get; }
        // Pub/Sub commands
        public RelayCommand CreatePublisherCommand { get; }
        public RelayCommand DestroyPublisherCommand { get; }
        public RelayCommand CreateSubscriberCommand { get; }
        public RelayCommand DestroySubscriberCommand { get; }

        // Traffic (messages)
        public sealed class TrafficItem
        {
            public string Header { get; init; } = "";
            public string Json { get; init; } = "";
        }
        public ObservableCollection<TrafficItem> Traffic { get; } = new();
        public RelayCommand ClearTrafficCommand { get; }
    public RelayCommand HandshakeCommand { get; }

        private readonly ClockService _clock;
    private readonly string? _autoConnectArg;
    private readonly string _debugLogPath;

        public MainViewModel(string configRoot, ClockService clock, string? autoConnectArg = null)
        {
            _clock = clock;
            _autoConnectArg = autoConnectArg;
            _debugLogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Agent.UI.Wpf.debug.log");
            // rotate old debug log
            try { if (System.IO.File.Exists(_debugLogPath)) System.IO.File.Delete(_debugLogPath); } catch { }
            // Default config root: if not provided, assume 'config' folder next to exe
            _configRoot = string.IsNullOrWhiteSpace(configRoot)
                ? System.IO.Path.Combine(AppContext.BaseDirectory, "config")
                : configRoot;

            // Wire service logging to ViewModel Log
            Agent.UI.Wpf.Services.ConfigService.LogAction = (s) => Log(s);

            // Instantiate AgentClient with UdpTransport + CborFrameCodec
            var transport = new UdpTransport();
            var codec = new CborMapCodec();

            // Wire service logs to ViewModel
            UdpTransport.LogAction = (s) => Log(s);
            CborMapCodec.LogAction = (s) => Log(s);
            AgentClient.LogAction = (s) => Log(s);

            _agent = new AgentClient(transport, codec);
            _agent.EventReceived += e =>
            {
                // marshal to UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(e.Data);
                    Traffic.Add(new TrafficItem
                    {
                        Header = $"[{_clock.Now():HH:mm:ss}] IN {e.Kind}",
                        Json = json
                    });
                });
            };

            // Connect/Disconnect now use AgentClient and perform handshake
            ConnectCommand = new RelayCommand(async () =>
            {
                try
                {
                    await _agent.ConnectAsync(Address, int.Parse(Port));
                    Log("Connected");

                    // automatic hello handshake
                    var helloReq = new Req { Op = "hello", Target = "agent", Args = null, Data = null };
                    AddOutTraffic("hello", new { op = helloReq.Op, target = helloReq.Target });

                    using var cts = new System.Threading.CancellationTokenSource(
                        System.TimeSpan.FromSeconds(Agent.UI.Wpf.Services.Timeouts.HelloSeconds)
                    );

                    var rsp = await _agent.RequestAsync(helloReq, cts.Token);
                    AddInTraffic("hello", new { ok = rsp.Ok, action = rsp.Action, data = rsp.Data, err = rsp.Err });
                    // Log full response for debugging
                    Log($"Handshake response: Ok={rsp.Ok} Action={rsp.Action} Err={rsp.Err} Data={System.Text.Json.JsonSerializer.Serialize(rsp.Data)}");

                    if (rsp.Ok)
                    {
                        Status = "Handshake OK";
                        Log("Handshake OK");
                    }
                    else
                    {
                        Status = "Handshake failed";
                        Log($"Handshake failed: {rsp.Err ?? "unknown error"}");
                    }
                }
                catch (System.OperationCanceledException)
                {
                    Status = "Handshake timeout";
                    Log("Handshake timeout");
                }
                catch (System.Exception ex)
                {
                    Status = "Handshake error";
                    Log($"Handshake error: {ex}");
                }
            });

            DisconnectCommand = new RelayCommand(async () =>
            {
                await _agent.DisconnectAsync();
                Log("Disconnected");
            });
            BrowseConfigCommand = new RelayCommand(BrowseConfig);
            ReloadConfigCommand = new RelayCommand(ReloadConfig);
            CreateParticipantCommand = new RelayCommand(() => Log($"Create Participant (domain={DomainId}, qos={SelectedQosProfile})"));
            ClearDdsCommand = new RelayCommand(() => Log("Clear DDS"));
            
            // Pub/Sub commands
            CreatePublisherCommand = new RelayCommand(() =>
            {
                Log($"[Publisher] create: name={PublisherName}, domain={DomainId}, qos={SelectedQosProfile}");
                // TODO: IPC - op=create, target=publisher
            });
            DestroyPublisherCommand = new RelayCommand(() => Log($"[Publisher] destroy: name={PublisherName}"));

            CreateSubscriberCommand = new RelayCommand(() =>
            {
                Log($"[Subscriber] create: name={SubscriberName}, domain={DomainId}, qos={SelectedQosProfile}");
                // TODO: IPC - op=create, target=subscriber
            });
            DestroySubscriberCommand = new RelayCommand(() => Log($"[Subscriber] destroy: name={SubscriberName}"));

            // Traffic
            ClearTrafficCommand = new RelayCommand(() => Traffic.Clear());
            CopyLogCommand = new RelayCommand(() =>
            {
                try
                {
                    var all = string.Join(Environment.NewLine, Logs);
                    System.Windows.Clipboard.SetText(all);
                    // update status briefly
                    var prev = Status;
                    Status = "Logs copied to clipboard";
                    // restore after 3 seconds without blocking UI
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        await System.Threading.Tasks.Task.Delay(3000);
                        Status = prev;
                    });
                }
                catch (Exception ex)
                {
                    Log($"Copy logs failed: {ex.Message}");
                }
            });
            // Handshake command (manual retry)
            HandshakeCommand = new RelayCommand(async () =>
            {
                try
                {
                    var req = new Req { Op = "hello", Target = "agent" };
                    AddOutTraffic("hello", new { op = req.Op, target = req.Target });

                    using var cts = new System.Threading.CancellationTokenSource(
                        System.TimeSpan.FromSeconds(Agent.UI.Wpf.Services.Timeouts.HelloSeconds)
                    );
                    var rsp = await _agent.RequestAsync(req, cts.Token);

                    AddInTraffic("hello", new { ok = rsp.Ok, action = rsp.Action, data = rsp.Data, err = rsp.Err });

                    Status = rsp.Ok ? "Handshake OK" : "Handshake failed";
                    Log(rsp.Ok ? "Handshake OK" : $"Handshake failed: {rsp.Err ?? "unknown error"}");
                }
                catch (System.OperationCanceledException)
                {
                    Status = "Handshake timeout";
                    Log("Handshake timeout");
                }
                catch (System.Exception ex)
                {
                    Status = "Handshake error";
                    Log($"Handshake error: {ex.Message}");
                }
            });
            OpenFormCommand = new RelayCommand(() => Log("Open dynamic form (TODO)"));
            FillSampleCommand = new RelayCommand(() => Payload = "{\n  \"sample\": true\n}");
            CreateWriterCommand = new RelayCommand(() => Log($"Create Writer on topic '{Topic}'"));
            CreateReaderCommand = new RelayCommand(() => Log($"Create Reader on topic '{Topic}'"));
            PublishCommand = new RelayCommand(() =>
            {
                Log($"Publish to '{Topic}' payload bytes={Payload.Length}");
                Traffic.Add(new TrafficItem
                {
                    Header = $"[{_clock.Now():HH:mm:ss}] OUT topic='{Topic}' type='{SelectedType}'",
                    Json = Payload
                });
            });
            ClearLogCommand = new RelayCommand(() => Logs.Clear());

            // Initial load from config (qos/topic/type)
            ReloadConfig();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _agent.DisposeAsync();
            }
            catch { }
        }

        private void Log(string msg)
        {
            Logs.Add($"[{_clock.Now():HH:mm:ss}] {msg}");
            Status = msg;
            try
            {
                System.IO.File.AppendAllText(_debugLogPath, $"[{DateTime.Now:O}] {msg}{Environment.NewLine}");
            }
            catch { }
        }

        private void AddOutTraffic(string action, object? payload)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            Traffic.Add(new TrafficItem
            {
                Header = $"[{_clock.Now():HH:mm:ss}] OUT {action}",
                Json = json
            });
        }

        private void AddInTraffic(string action, object? payload)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            Traffic.Add(new TrafficItem
            {
                Header = $"[{_clock.Now():HH:mm:ss}] IN {action}",
                Json = json
            });
        }

        private void BrowseConfig()
        {
            // Use FolderBrowserDialog so users can pick a folder directly
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select your config directory",
                SelectedPath = ConfigRoot
            };

            var res = dlg.ShowDialog();
            if (res == System.Windows.Forms.DialogResult.OK || res == System.Windows.Forms.DialogResult.Yes)
            {
                ConfigRoot = dlg.SelectedPath;
                Log($"Config set: {ConfigRoot}");
                ReloadConfig();
            }
        }

        private void ReloadConfig()
        {
            try
            {
                QosProfiles.Clear();
                TypeNames.Clear();

                var profiles = ConfigService.LoadQosProfiles(_configRoot);
                if (profiles.Count == 0)
                {
                    Log("No QoS profiles found in config");
                }
                else
                {
                    foreach (var p in profiles) QosProfiles.Add(p);
                    SelectedQosProfile = QosProfiles.Count > 0 ? QosProfiles[0] : null;
                }

                var types = ConfigService.LoadTypeNames(_configRoot);
                if (types.Count == 0)
                {
                    Log("No type definitions found in config");
                }
                else
                {
                    foreach (var t in types) TypeNames.Add(t);
                    SelectedType = TypeNames.Count > 0 ? TypeNames[0] : null;
                    // If topic empty, set a sensible default derived from selected type
                    if (string.IsNullOrWhiteSpace(Topic) && SelectedType != null)
                    {
                        Topic = SelectedType.StartsWith("C_") ? SelectedType.Substring(2) : SelectedType;
                    }
                }

                Log("Config reloaded");
            }
            catch (Exception ex)
            {
                Log($"Config load failed: {ex.Message}");
            }
        }
    }
}
