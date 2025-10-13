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

    // Additional inputs per create-guide
    private string _participantName = "";
    public string ParticipantName { get => _participantName; set => SetField(ref _participantName, value); }

    private string _participantRef = "";
    public string ParticipantRef { get => _participantRef; set => SetField(ref _participantRef, value); }

    private string _writerName = "";
    public string WriterName { get => _writerName; set => SetField(ref _writerName, value); }

    private string _readerName = "";
    public string ReaderName { get => _readerName; set => SetField(ref _readerName, value); }

    private string _topicName = "";
    public string TopicName { get => _topicName; set => SetField(ref _topicName, value); }

    private string _typeName = "";
    public string TypeName { get => _typeName; set => SetField(ref _typeName, value); }

        public ObservableCollection<string> QosProfiles { get; } = new();
        private string? _selectedQosProfile;
        public string? SelectedQosProfile { get => _selectedQosProfile; set => SetField(ref _selectedQosProfile, value); }
    private string _qosLibrary = "";
    public string QosLibrary { get => _qosLibrary; set => SetField(ref _qosLibrary, value); }

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
                        // SelectedType might be 'Module::C_Name' or just 'C_Name' or 'Name'
                        var t = _selectedType;
                        var idx = t.IndexOf("::");
                        if (idx >= 0) t = t.Substring(idx + 2);
                        var baseTopic = t.StartsWith("C_") ? t.Substring(2) : t;
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
    private string _logsText = "";
    public string LogsText { get => _logsText; private set => SetField(ref _logsText, value); }

        private string _status = "Ready";
        public string Status { get => _status; set => SetField(ref _status, value); }

        // Commands
        public RelayCommand ConnectCommand { get; }
        public RelayCommand DisconnectCommand { get; }
        public RelayCommand BrowseConfigCommand { get; }
        public RelayCommand ReloadConfigCommand { get; }
        public AsyncRelayCommand CreateParticipantCommand { get; }
        public AsyncRelayCommand ClearDdsCommand { get; }
        public RelayCommand OpenFormCommand { get; }
        public RelayCommand FillSampleCommand { get; }
        public AsyncRelayCommand CreateWriterCommand { get; }
        public AsyncRelayCommand CreateReaderCommand { get; }
        public RelayCommand PublishCommand { get; }
        public RelayCommand ClearLogCommand { get; }
    public RelayCommand CopyLogCommand { get; }
        // Pub/Sub commands
        public AsyncRelayCommand CreatePublisherCommand { get; }
        public RelayCommand DestroyPublisherCommand { get; }
        public AsyncRelayCommand CreateSubscriberCommand { get; }
        public RelayCommand DestroySubscriberCommand { get; }

        // Traffic (messages)
        public sealed class TrafficItem
        {
            public string Header { get; init; } = "";
            public string Json { get; init; } = "";
            public bool IsInbound { get; init; }
        }
        public ObservableCollection<TrafficItem> Traffic { get; } = new();
    private string _trafficText = "";
    public string TrafficText { get => _trafficText; private set => SetField(ref _trafficText, value); }
        public RelayCommand ClearTrafficCommand { get; }
    public RelayCommand HandshakeCommand { get; }

        private readonly ClockService _clock;
    private readonly string? _autoConnectArg;
    private readonly string _debugLogPath;

        // Create timeout (from Timeouts.CreateSeconds)
        private static readonly TimeSpan CreateTimeout = TimeSpan.FromSeconds(Agent.UI.Wpf.Services.Timeouts.CreateSeconds);

        // Validation helpers
        private bool IsNullOrWhite(string? s) => string.IsNullOrWhiteSpace(s);
        private void Require(bool cond, string msg) { if (!cond) throw new InvalidOperationException(msg); }

        // QoS builder: prefer SelectedQosProfile (already may be 'Lib::Profile'), fallback to QosLibrary::
        private string BuildQos()
        {
            if (!string.IsNullOrWhiteSpace(SelectedQosProfile)) return SelectedQosProfile!;
            if (!string.IsNullOrWhiteSpace(QosLibrary)) return QosLibrary + "::";
            return string.Empty;
        }

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
                    // pretty-print event data
                    var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                    var json = System.Text.Json.JsonSerializer.Serialize(e.Data, options);
                    Traffic.Add(new TrafficItem
                    {
                        Header = $"[{_clock.Now():HH:mm:ss}] IN {e.Kind}",
                        Json = json,
                        IsInbound = true
                    });
                    UpdateTrafficText();
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
            CreateParticipantCommand = new AsyncRelayCommand(async () =>
            {
                try
                {
                    // validation
                    if (!int.TryParse(DomainId, out var d)) throw new InvalidOperationException("DomainId가 필요합니다");
                    Require(d >= 0, "DomainId가 필요합니다");

                    var qos = BuildQos();
                    var args = new System.Collections.Generic.Dictionary<string, object?>
                    {
                        ["domain"] = d,
                        ["qos"] = qos
                    };

                    var req = new Req {
                        Op = "create",
                        Target = "participant",
                        TargetExtra = null,
                        Args = args,
                        Data = null
                    };

                    AddOutTraffic("create.participant", req);
                    using var cts = new System.Threading.CancellationTokenSource(CreateTimeout);
                    var rsp = await _agent.RequestAsync(req, cts.Token);
                    AddInTraffic("create.participant", rsp);

                    if (rsp.Ok) { Status = "Participant created"; Log("Participant created"); }
                    else { Status = "Create failed"; Log($"Create participant failed: {rsp.Err ?? "unknown"}"); }
                }
                catch (OperationCanceledException)
                {
                    Status = "Create timeout"; Log("Create participant timeout");
                }
                catch (Exception ex)
                {
                    Status = "Create error"; Log($"Create participant error: {ex.Message}");
                }
            }, null, ex => Log($"CreateParticipantCommand exception: {ex.Message}"));

            ClearDdsCommand = new AsyncRelayCommand(async () =>
            {
                try
                {
                    var req = new Req { Op = "clear", Target = "dds_entities", TargetExtra = null, Args = null, Data = null };
                    AddOutTraffic("clear", req);
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var rsp = await _agent.RequestAsync(req, cts.Token);
                    AddInTraffic("clear", rsp);
                    if (rsp.Ok) { Status = "All cleared"; Log("All cleared"); }
                    else { Status = "Clear failed"; Log($"Clear failed: {rsp.Err ?? "unknown"}"); }
                }
                catch (OperationCanceledException) { Status = "Clear timeout"; Log("Clear timeout"); }
                catch (Exception ex) { Status = "Clear error"; Log($"Clear error: {ex.Message}"); }
            }, null, ex => Log($"ClearDdsCommand exception: {ex.Message}"));
            
            // Pub/Sub commands
            CreatePublisherCommand = new AsyncRelayCommand(async () =>
            {
                try
                {
                    if (!int.TryParse(DomainId, out var d)) throw new InvalidOperationException("DomainId가 필요합니다");
                    Require(d >= 0, "Domain 필요");
                    Require(!IsNullOrWhite(PublisherName), "Publisher 이름 필요");
                    var qos = BuildQos();

                    var args = new System.Collections.Generic.Dictionary<string, object?>
                    {
                        ["domain"] = d,
                        ["publisher"] = PublisherName,
                        ["qos"] = qos
                    };

                    var req = new Req { Op = "create", Target = "publisher", TargetExtra = null, Args = args, Data = null };
                    AddOutTraffic("create.publisher", req);
                    using var cts = new System.Threading.CancellationTokenSource(CreateTimeout);
                    var rsp = await _agent.RequestAsync(req, cts.Token);
                    AddInTraffic("create.publisher", rsp);
                    if (rsp.Ok) { Status = "Publisher created"; Log("Publisher created"); }
                    else { Status = "Create failed"; Log($"Create publisher failed: {rsp.Err ?? "unknown"}"); }
                }
                catch (OperationCanceledException) { Status = "Create timeout"; Log("Create publisher timeout"); }
                catch (Exception ex) { Status = "Create error"; Log($"Create publisher error: {ex.Message}"); }
            }, null, ex => Log($"CreatePublisherCommand exception: {ex.Message}"));
            DestroyPublisherCommand = new RelayCommand(() => Log($"[Publisher] destroy: name={PublisherName}"));

            CreateSubscriberCommand = new AsyncRelayCommand(async () =>
            {
                try
                {
                    if (!int.TryParse(DomainId, out var d)) throw new InvalidOperationException("DomainId가 필요합니다");
                    Require(d >= 0, "Domain 필요");
                    Require(!IsNullOrWhite(SubscriberName), "Subscriber 이름 필요");
                    var qos = BuildQos();

                    var args = new System.Collections.Generic.Dictionary<string, object?>
                    {
                        ["domain"] = d,
                        ["subscriber"] = SubscriberName,
                        ["qos"] = qos
                    };

                    var req = new Req { Op = "create", Target = "subscriber", TargetExtra = null, Args = args, Data = null };
                    AddOutTraffic("create.subscriber", req);
                    using var cts = new System.Threading.CancellationTokenSource(CreateTimeout);
                    var rsp = await _agent.RequestAsync(req, cts.Token);
                    AddInTraffic("create.subscriber", rsp);
                    if (rsp.Ok) { Status = "Subscriber created"; Log("Subscriber created"); }
                    else { Status = "Create failed"; Log($"Create subscriber failed: {rsp.Err ?? "unknown"}"); }
                }
                catch (OperationCanceledException) { Status = "Create timeout"; Log("Create subscriber timeout"); }
                catch (Exception ex) { Status = "Create error"; Log($"Create subscriber error: {ex.Message}"); }
            }, null, ex => Log($"CreateSubscriberCommand exception: {ex.Message}"));
            DestroySubscriberCommand = new RelayCommand(() => Log($"[Subscriber] destroy: name={SubscriberName}"));

            // Traffic
            ClearTrafficCommand = new RelayCommand(() => { Traffic.Clear(); TrafficText = string.Empty; });
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
            CreateWriterCommand = new AsyncRelayCommand(async () =>
            {
                try
                {
                    if (!int.TryParse(DomainId, out var d)) throw new InvalidOperationException("DomainId가 필요합니다");
                    Require(d >= 0, "Domain 필요");
                    Require(!IsNullOrWhite(PublisherName), "Publisher 이름 필요");
                    var tname = !IsNullOrWhite(TopicName) ? TopicName : Topic;
                    var typename = !IsNullOrWhite(TypeName) ? TypeName : SelectedType;
                    Require(!IsNullOrWhite(tname), "Topic 필요");
                    Require(!IsNullOrWhite(typename), "Type 필요 (예: C_*)");
                    var qos = BuildQos();

                    var targetExtra = new System.Collections.Generic.Dictionary<string, object?> {
                        ["topic"] = tname,
                        ["type"]  = typename
                    };

                    var args = new System.Collections.Generic.Dictionary<string, object?>
                    {
                        ["domain"] = d,
                        ["publisher"] = PublisherName,
                        ["qos"] = qos
                    };

                    var req = new Req { Op = "create", Target = "writer", TargetExtra = targetExtra, Args = args, Data = null };
                    AddOutTraffic("create.writer", req);
                    using var cts = new System.Threading.CancellationTokenSource(CreateTimeout);
                    var rsp = await _agent.RequestAsync(req, cts.Token);
                    AddInTraffic("create.writer", rsp);
                    if (rsp.Ok) { Status = "Writer created"; Log("Writer created"); }
                    else { Status = "Create failed"; Log($"Create writer failed: {rsp.Err ?? "unknown"}"); }
                }
                catch (OperationCanceledException) { Status = "Create timeout"; Log("Create writer timeout"); }
                catch (Exception ex) { Status = "Create error"; Log($"Create writer error: {ex.Message}"); }
            }, null, ex => Log($"CreateWriterCommand exception: {ex.Message}"));

            CreateReaderCommand = new AsyncRelayCommand(async () =>
            {
                try
                {
                    if (!int.TryParse(DomainId, out var d)) throw new InvalidOperationException("DomainId가 필요합니다");
                    Require(d >= 0, "Domain 필요");
                    Require(!IsNullOrWhite(SubscriberName), "Subscriber 이름 필요");
                    var tname = !IsNullOrWhite(TopicName) ? TopicName : Topic;
                    var typename = !IsNullOrWhite(TypeName) ? TypeName : SelectedType;
                    Require(!IsNullOrWhite(tname), "Topic 필요");
                    Require(!IsNullOrWhite(typename), "Type 필요 (예: C_*)");
                    var qos = BuildQos();

                    var targetExtra = new System.Collections.Generic.Dictionary<string, object?> {
                        ["topic"] = tname,
                        ["type"]  = typename
                    };

                    var args = new System.Collections.Generic.Dictionary<string, object?>
                    {
                        ["domain"] = d,
                        ["subscriber"] = SubscriberName,
                        ["qos"] = qos
                    };

                    var req = new Req { Op = "create", Target = "reader", TargetExtra = targetExtra, Args = args, Data = null };
                    AddOutTraffic("create.reader", req);
                    using var cts = new System.Threading.CancellationTokenSource(CreateTimeout);
                    var rsp = await _agent.RequestAsync(req, cts.Token);
                    AddInTraffic("create.reader", rsp);
                    if (rsp.Ok) { Status = "Reader created"; Log("Reader created"); }
                    else { Status = "Create failed"; Log($"Create reader failed: {rsp.Err ?? "unknown"}"); }
                }
                catch (OperationCanceledException) { Status = "Create timeout"; Log("Create reader timeout"); }
                catch (Exception ex) { Status = "Create error"; Log($"Create reader error: {ex.Message}"); }
            }, null, ex => Log($"CreateReaderCommand exception: {ex.Message}"));
            PublishCommand = new RelayCommand(() =>
            {
                Log($"Publish to '{Topic}' payload bytes={Payload.Length}");
                Traffic.Add(new TrafficItem
                {
                    Header = $"[{_clock.Now():HH:mm:ss}] OUT topic='{Topic}' type='{SelectedType}'",
                    Json = Payload
                });
            });
            ClearLogCommand = new RelayCommand(() => { Logs.Clear(); LogsText = string.Empty; });

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
            // update aggregated LogsText for multi-line selection
            try { LogsText = string.Join(Environment.NewLine, Logs); } catch { }
            try
            {
                System.IO.File.AppendAllText(_debugLogPath, $"[{DateTime.Now:O}] {msg}{Environment.NewLine}");
            }
            catch { }
        }

        private void AddOutTraffic(string action, object? payload)
        {
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            var json = System.Text.Json.JsonSerializer.Serialize(payload, options);
            Traffic.Add(new TrafficItem
            {
                Header = $"[{_clock.Now():HH:mm:ss}] OUT {action}",
                Json = json,
                IsInbound = false
            });
            try { UpdateTrafficText(); } catch { }
        }

        private void AddInTraffic(string action, object? payload)
        {
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            var json = System.Text.Json.JsonSerializer.Serialize(payload, options);
            Traffic.Add(new TrafficItem
            {
                Header = $"[{_clock.Now():HH:mm:ss}] IN {action}",
                Json = json,
                IsInbound = true
            });
            try { UpdateTrafficText(); } catch { }
        }

        private void UpdateTrafficText()
        {
            try
            {
                var lines = new System.Text.StringBuilder();
                foreach (var t in Traffic)
                {
                    lines.AppendLine(t.Header);
                    lines.AppendLine(t.Json);
                    lines.AppendLine();
                }
                TrafficText = lines.ToString();
            }
            catch { }
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
