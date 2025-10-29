using System;
using System.Collections.ObjectModel;
using Microsoft.Win32;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Linq;
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
    /// <summary>QoS 이름별 상세 JSON(문자열) 저장</summary>
    public System.Collections.Generic.Dictionary<string, string> QosDetails { get; } = new();
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
                    // Also auto-generate sample payload from schema cache
                    try
                    {
                        var tname = _selectedType;
                        if (!string.IsNullOrWhiteSpace(tname))
                        {
                            var sample = _sampleBuilder.BuildSample(tname!);
                            Payload = System.Text.Json.JsonSerializer.Serialize(sample, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                            Status = "Sample JSON updated";
                        }
                    }
                    catch (Exception ex)
                    {
                        // don't break UI; log the issue
                        Log($"Sample generation failed: {ex.Message}");
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
        public string[] LogLevels { get; } = new[] { "Info", "Trace", "Debug" };
        private string _selectedLogLevel = "Info";
        public string SelectedLogLevel
        {
            get => _selectedLogLevel;
            set
            {
                if (SetField(ref _selectedLogLevel, value))
                {
                    // map to Logger level
                    switch (_selectedLogLevel?.ToLowerInvariant())
                    {
                        case "debug": Services.Logger.CurrentLevel = Services.Logger.Level.Debug; break;
                        case "trace": Services.Logger.CurrentLevel = Services.Logger.Level.Trace; break;
                        default: Services.Logger.CurrentLevel = Services.Logger.Level.Info; break;
                    }
                }
            }
        }
        public ObservableCollection<string> Logs { get; } = new();
    private string _logsText = "";
    public string LogsText { get => _logsText; private set => SetField(ref _logsText, value); }

        private string _status = "Ready";
        public string Status { get => _status; set => SetField(ref _status, value); }

    // Commands
    public AsyncRelayCommand ToggleConnectionCommand { get; }
    /// <summary>사용자 트리거로 QoS 재요청</summary>
    public AsyncRelayCommand RefreshQosCommand { get; }
    public RelayCommand ShowQosDetailCommand { get; }
    /// <summary>XML 파일에서 QoS를 불러와 QosProfiles/QosDetails를 대체</summary>
    public RelayCommand LoadQosFromFileCommand { get; }
    public RelayCommand BrowseConfigCommand { get; }
        public RelayCommand ReloadConfigCommand { get; }
        public AsyncRelayCommand CreateParticipantCommand { get; }
        public AsyncRelayCommand ClearDdsCommand { get; }
        public RelayCommand OpenFormCommand { get; }
        public RelayCommand FillSampleCommand { get; }
    public RelayCommand SampleJsonCommand { get; }
        public AsyncRelayCommand CreateWriterCommand { get; }
        public AsyncRelayCommand CreateReaderCommand { get; }
        public RelayCommand PublishCommand { get; }
        public RelayCommand ClearLogCommand { get; }
    public ParameterizedRelayCommand CopySelectedLogCommand { get; }
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
            /// <summary>원본(송수신) 페이로드(가능하면 원문 JSON 텍스트). 로그에 출력되는 내용의 원본입니다.</summary>
            public string Raw { get; init; } = "";
            /// <summary>UI의 Messages 탭에 표시할 pretty-printed JSON(읽기 전용). 원본을 변경하지 않음.</summary>
            public string Pretty { get; init; } = "";
            public bool IsInbound { get; init; }
        }
        public ObservableCollection<TrafficItem> Traffic { get; } = new();
    private string _trafficText = "";
    public string TrafficText { get => _trafficText; private set => SetField(ref _trafficText, value); }
        public RelayCommand ClearTrafficCommand { get; }
    public RelayCommand HandshakeCommand { get; }
    // MVVM-friendly command to copy selected messages from the view.
    public ParameterizedRelayCommand CopySelectedMessagesCommand { get; }

    private readonly ClockService _clock;
    private readonly string? _autoConnectArg;
    private readonly string _debugLogPath;
    private readonly Services.ITypeSchemaProvider _typeProvider;
    private readonly Services.SampleJsonBuilder _sampleBuilder;
        
    // QoS load state: 서버에서 get qos 응답을 받아 콤보박스가 완성되었는지
    private bool _isQosLoaded = false;
    /// <summary>QoS 목록을 서버에서 정상 수신하여 UI가 활성화 가능한 상태인지</summary>
    public bool IsQosLoaded { get => _isQosLoaded; private set { if (SetField(ref _isQosLoaded, value)) Raise(nameof(CanUseDds)); } }

    /// <summary>DDS 관련 UI가 활성화될 수 있는지 여부 (연결됨 && QoS 목록 수신됨)</summary>
    public bool CanUseDds => IsConnected && IsQosLoaded;

        // Connection state for toggle button
        private bool _isConnected = false;
        /// <summary>
        /// 현재 연결 상태
        /// </summary>
        public bool IsConnected { get => _isConnected; private set { if (SetField(ref _isConnected, value)) RaisePropertyChangedForConnection(); } }

        private void RaisePropertyChangedForConnection()
        {
            // notify UI for text change
            Raise(nameof(ConnectionButtonText));
            // Notify RefreshQosCommand that CanExecute may have changed
            try { RefreshQosCommand?.RaiseCanExecuteChanged(); } catch { }
        }

        /// <summary>
        /// 토글 버튼에 바인딩할 텍스트
        /// </summary>
        public string ConnectionButtonText => IsConnected ? "Disconnect" : "Connect";

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

            // Wire service logging to unified Logger which forwards to this ViewModel
            Services.Logger.Sink = s => Log(s);
            Services.Logger.AcceptExternal("startup");

            // Instantiate AgentClient with UdpTransport + CborFrameCodec
            var transport = new UdpTransport();
            var codec = new CborMapCodec();

            // Keep existing per-service callbacks but route to Logger.AcceptExternal to preserve behavior
            Agent.UI.Wpf.Services.ConfigService.LogAction = (s) => Services.Logger.AcceptExternal(s);
            UdpTransport.LogAction = (s) => Services.Logger.AcceptExternal(s);
            CborMapCodec.LogAction = (s) => Services.Logger.AcceptExternal(s);
            AgentClient.LogAction = (s) => Services.Logger.AcceptExternal(s);

            _agent = new AgentClient(transport, codec);
            _agent.EventReceived += e =>
            {
                // marshal to UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // Do not alter original payload formatting; emit raw JSON when available.
                        string json;
                        try
                        {
                            if (e.Data is string s) json = s;
                            else json = System.Text.Json.JsonSerializer.Serialize(e.Data, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
                        }
                        catch
                        {
                            // fallback to a safe serialization
                            json = System.Text.Json.JsonSerializer.Serialize(e.Data);
                        }
                        // store both raw (for Logs) and pretty (for Messages tab)
                        var prettyJson = json;
                        try { prettyJson = System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Deserialize<object>(json), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }); } catch { }
                        // Add to UI traffic (Messages tab) and emit a concise TRACE entry (no raw payload)
                        var rawTruncated = Services.Logger.TruncateUtf8(json);
                        Traffic.Add(new TrafficItem
                        {
                            Header = $"[{_clock.Now():HH:mm:ss}] IN {e.Kind}",
                            Raw = rawTruncated,
                            Pretty = prettyJson,
                            IsInbound = true
                        });
                        Services.Logger.Trace($"IN {e.Kind} len={json.Length}");
                        UpdateTrafficText();

                        // 강화: data 이벤트는 로그와 Payload 미러링(선택적) 처리
                        if (string.Equals(e.Kind, "data", StringComparison.OrdinalIgnoreCase))
                        {
                            // 전체 JSON은 Traffic/Logs에 남기고, 상태바에는 간단한 한줄 요약만 표시합니다.
                            string summary = string.Empty;
                            try
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(json);
                                var root = doc.RootElement;
                                if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
                                {
                                    if (root.TryGetProperty("topic", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String)
                                        summary = p.GetString() ?? string.Empty;
                                    else if (root.TryGetProperty("type", out var p2) && p2.ValueKind == System.Text.Json.JsonValueKind.String)
                                        summary = p2.GetString() ?? string.Empty;
                                    else
                                    {
                                        foreach (var prop in root.EnumerateObject()) { summary = prop.Name; break; }
                                    }
                                }
                            }
                            catch { }

                            if (string.IsNullOrWhiteSpace(summary)) summary = "data";
                            // Log concise event and set a single-line status
                            Log($"EVT data: {summary}");
                            Status = $"IN {summary}";
                            // 미러링 동작은 주석 처리된 형태로 보존; 필요 시 활성화
                            // Payload = json;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"EventReceived 처리 오류: {ex.Message}");
                    }
                });
            };

            // Toggle connection command: connects when disconnected, disconnects when connected
            ToggleConnectionCommand = new AsyncRelayCommand(async () =>
            {
                if (!_isConnected)
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

                        IsConnected = true;
                        // 연결 성공 후 자동으로 QoS 목록을 요청하여 콤보박스를 서버 응답으로 채웁니다.
                        try
                        {
                            using var ctsQ = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                            var ok = await TryLoadQosFromServerAsync(ctsQ.Token);
                            if (ok) Log("QoS loaded after handshake");
                        }
                        catch (Exception ex)
                        {
                            Log($"get qos error after handshake: {ex.Message}; keeping local config");
                            IsQosLoaded = false;
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
                }
                else
                {
                    try
                    {
                        await _agent.DisconnectAsync();
                        Log("Disconnected");
                    }
                    finally
                    {
                        IsConnected = false;
                    }
                }
            }, null, ex => Log($"ToggleConnectionCommand exception: {ex.Message}"));
            BrowseConfigCommand = new RelayCommand(() =>
            {
                try
                {
                    if (System.OperatingSystem.IsWindows()) BrowseConfigAction?.Invoke();
                }
                catch { }
            });
            LoadQosFromFileCommand = new RelayCommand(LoadQosFromFile);
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
            CopySelectedMessagesCommand = new ParameterizedRelayCommand(param =>
            {
                try
                {
                    if (param is System.Collections.IEnumerable items)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var obj in items)
                        {
                            if (obj is TrafficItem ti)
                            {
                                sb.AppendLine(ti.Header);
                                sb.AppendLine(ti.Raw);
                                sb.AppendLine();
                            }
                        }
                        if (sb.Length > 0) System.Windows.Clipboard.SetText(sb.ToString());
                    }
                }
                catch (System.Exception ex)
                {
                    Log($"Copy selected messages failed: {ex.Message}");
                }
            });
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
            CopySelectedLogCommand = new ParameterizedRelayCommand(param =>
            {
                try
                {
                    if (param is System.Collections.IEnumerable items)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var it in items)
                        {
                            if (it is string s) { sb.AppendLine(s); }
                        }
                        if (sb.Length > 0) System.Windows.Clipboard.SetText(sb.ToString());
                    }
                }
                catch (Exception ex)
                {
                    Log($"Copy selected logs failed: {ex.Message}");
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

            // Refresh QoS command (user-initiated)
            RefreshQosCommand = new AsyncRelayCommand(async () =>
            {
                try
                {
                    // confirm with user
                    var res = System.Windows.MessageBox.Show("서버에서 QoS 목록을 다시 가져오시겠습니까?\n(현재 DDS UI는 재요청 중 비활성화됩니다)", "Refresh QoS", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                    if (res != System.Windows.MessageBoxResult.Yes) return;

                    // disable UX until done
                    IsQosLoaded = false;

                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var ok = await TryLoadQosFromServerAsync(cts.Token);
                    if (ok) Log("Manual QoS refresh succeeded");
                    else Log("Manual QoS refresh failed; keeping local config");
                }
                catch (Exception ex)
                {
                    Log($"RefreshQosCommand error: {ex.Message}");
                }
            }, () => IsConnected, ex => Log($"RefreshQosCommand exception: {ex.Message}"));

            ShowQosDetailCommand = new RelayCommand(() =>
            {
                try
                {
                    var sel = SelectedQosProfile;
                    if (string.IsNullOrWhiteSpace(sel)) { System.Windows.MessageBox.Show("QoS가 선택되지 않았습니다.", "QoS Detail"); return; }
                    if (!QosDetails.TryGetValue(sel, out var json)) { System.Windows.MessageBox.Show("상세 정보가 없습니다.", "QoS Detail"); return; }
                    // Show in a simple window
                    var w = new Views.QosDetailWindow(sel, json) { Owner = System.Windows.Application.Current?.MainWindow };
                    w.ShowDialog();
                }
                catch (Exception ex)
                {
                    Log($"ShowQosDetailCommand error: {ex.Message}");
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
            PublishCommand = new RelayCommand(async () =>
            {
                try
                {
                    // 입력 검사
                    if (!int.TryParse(DomainId, out var d)) throw new InvalidOperationException("DomainId가 필요합니다");
                    Require(d >= 0, "Domain 필요");
                    Require(!IsNullOrWhite(PublisherName), "Publisher 이름 필요");
                    var tname = !IsNullOrWhite(TopicName) ? TopicName : Topic;
                    var typename = !IsNullOrWhite(TypeName) ? TypeName : SelectedType;
                    Require(!IsNullOrWhite(tname), "Topic 필요");
                    Require(!IsNullOrWhite(typename), "Type 필요 (예: C_*)");

                    // 1) Payload JSON 파싱 및 compact 문자열 생성
                    var jsonObj = ParseJsonOrThrow(Payload);
                    var compact = ToCompactJsonString(jsonObj);

                    // 2) Req 작성 (Gateway 계약: data.text = JSON 문자열)
                    var qos = BuildQos();
                    var targetExtra = new System.Collections.Generic.Dictionary<string, object?> {
                        ["topic"] = tname,
                        ["type"] = typename
                    };
                    var args = new System.Collections.Generic.Dictionary<string, object?> {
                        ["domain"] = d,
                        ["publisher"] = PublisherName,
                        ["qos"] = qos
                    };
                    var data = new { text = compact };

                    var req = new Req
                    {
                        Op = "write",
                        Target = "writer",
                        TargetExtra = targetExtra,
                        Args = args,
                        Data = data
                    };

                    AddOutTraffic("write", new { target = targetExtra, args, data });

                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var rsp = await _agent.RequestAsync(req, cts.Token);

                    AddInTraffic("write", new { ok = rsp.Ok, action = rsp.Action, data = rsp.Data, err = rsp.Err });
                    Status = rsp.Ok ? "Write OK" : $"Write failed: {rsp.Err ?? "unknown"}";
                }
                catch (OperationCanceledException)
                {
                    Status = "Write timeout"; Log("Write timeout");
                }
                catch (Exception ex)
                {
                    Status = "Write error"; Log($"Write error: {ex.Message}");
                }
            });

            // JSON helpers used by PublishCommand
            static object ParseJsonOrThrow(string text)
            {
                try
                {
                    return System.Text.Json.JsonSerializer.Deserialize<object>(text)
                           ?? throw new InvalidOperationException("빈 JSON");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Payload JSON 오류: {ex.Message}");
                }
            }

            static string ToCompactJsonString(object node)
                => System.Text.Json.JsonSerializer.Serialize(node, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
            ClearLogCommand = new RelayCommand(() => { Logs.Clear(); LogsText = string.Empty; });

            // Instantiate type schema provider and sample builder (parses config/generated once)
            _typeProvider = new Services.XmlTypeSchemaProvider(_configRoot);
            _sampleBuilder = new Services.SampleJsonBuilder(_typeProvider);

            // populate TypeNames from provider (cached list)
            try
            {
                TypeNames.Clear();
                foreach (var tn in _typeProvider.GetTypeNames()) TypeNames.Add(tn);
                if (TypeNames.Count > 0 && string.IsNullOrWhiteSpace(SelectedType)) SelectedType = TypeNames[0];
            }
            catch (Exception ex) { Log($"Type list load failed: {ex.Message}"); }

            // Sample JSON command
            SampleJsonCommand = new RelayCommand(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(SelectedType) && string.IsNullOrWhiteSpace(TypeName)) throw new InvalidOperationException("Type 필요");
                    var tname = !string.IsNullOrWhiteSpace(TypeName) ? TypeName : SelectedType!;
                    var sample = _sampleBuilder.BuildSample(tname);
                    Payload = System.Text.Json.JsonSerializer.Serialize(sample, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    Status = "Sample JSON generated";
                }
                catch (Exception ex)
                {
                    Status = "Sample JSON error";
                    Log($"Sample JSON error: {ex.Message}");
                }
            });

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

        /// <summary>
        /// 서버에 get.qos 요청을 보내고 QosProfiles 컬렉션을 갱신합니다.
        /// 반환값: 성공적으로 QoS를 채웠으면 true
        /// </summary>
        private async System.Threading.Tasks.Task<bool> TryLoadQosFromServerAsync(System.Threading.CancellationToken ct)
        {
            try
            {
                // Build request as: { op: "get", target: { kind: "qos" }, args: { include_builtin: true, detail: true } }
                var getReq = new Req
                {
                    Op = "get",
                    Target = new System.Collections.Generic.Dictionary<string, object?> { ["kind"] = "qos" },
                    Args = new System.Collections.Generic.Dictionary<string, object?> { ["include_builtin"] = true, ["detail"] = true }
                };
                AddOutTraffic("get.qos", new { op = getReq.Op, target = getReq.Target, args = getReq.Args });
                var getRsp = await _agent.RequestAsync(getReq, ct);
                AddInTraffic("get.qos", new { ok = getRsp.Ok, result = getRsp.Data, err = getRsp.Err });

                if (!getRsp.Ok) return false;

                    // Debug: log the runtime type of getRsp.Data to diagnose parsing branches
                    try
                    {
                        var dt = getRsp.Data?.GetType().FullName ?? "<null>";
                        Services.Logger.Debug($"TryLoadQos: getRsp.Data runtime type = {dt}");
                        // if serializable to JSON, print a small preview
                        try
                        {
                            var preview = System.Text.Json.JsonSerializer.Serialize(getRsp.Data);
                            Services.Logger.Debug($"TryLoadQos: preview={preview?.Substring(0, Math.Min(512, preview.Length))}");
                        }
                        catch { }
                    }
                    catch { }

                bool filled = false;
                try
                {
                    // Case A: JsonElement
                    if (getRsp.Data is System.Text.Json.JsonElement je)
                    {
                        if (je.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            QosProfiles.Clear();
                            foreach (var el in je.EnumerateArray()) if (el.ValueKind == System.Text.Json.JsonValueKind.String) { var s = el.GetString(); if (s!=null) QosProfiles.Add(s); }
                            filled = QosProfiles.Count > 0;
                        }
                        else if (je.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            if (je.TryGetProperty("result", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                QosProfiles.Clear();
                                foreach (var el in r.EnumerateArray()) if (el.ValueKind == System.Text.Json.JsonValueKind.String) { var s = el.GetString(); if (s!=null) QosProfiles.Add(s); }
                                filled = QosProfiles.Count > 0;
                            }
                            // detail 처리: je.detail -> store pretty JSON per qos
                            if (je.TryGetProperty("detail", out var detail))
                            {
                                try
                                {
                                    QosDetails.Clear();
                                    if (detail.ValueKind == System.Text.Json.JsonValueKind.Object)
                                    {
                                        // detail is a map: { name: {...}, ... }
                                        foreach (var prop in detail.EnumerateObject())
                                        {
                                            var pretty = System.Text.Json.JsonSerializer.Serialize(prop.Value, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                                            QosDetails[prop.Name] = pretty;
                                        }
                                    }
                                    else if (detail.ValueKind == System.Text.Json.JsonValueKind.Array)
                                    {
                                        // detail is an array of single-entry objects: [ { name: {...} }, ... ]
                                        foreach (var item in detail.EnumerateArray())
                                        {
                                            if (item.ValueKind == System.Text.Json.JsonValueKind.Object)
                                            {
                                                foreach (var prop in item.EnumerateObject())
                                                {
                                                    var pretty = System.Text.Json.JsonSerializer.Serialize(prop.Value, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                                                    QosDetails[prop.Name] = pretty;
                                                }
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    // Case B: IEnumerable (but exclude IDictionary which is handled below)
                    if (!filled && getRsp.Data is System.Collections.IEnumerable ie && !(getRsp.Data is System.Collections.IDictionary))
                    {
                        QosProfiles.Clear();
                        foreach (var it in ie)
                        {
                            if (it is string ss) QosProfiles.Add(ss);
                            else if (it is System.Text.Json.JsonElement jel && jel.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                var s = jel.GetString(); if (s!=null) QosProfiles.Add(s);
                            }
                            else
                            {
                                // unknown element type: skip to avoid inserting detail/complex objects into profiles
                            }
                        }
                        filled = QosProfiles.Count > 0;
                    }

                    // Case C: IDictionary with "result"
                    if (!filled && getRsp.Data is System.Collections.IDictionary dict && dict.Contains("result"))
                    {
                        try
                        {
                            var res = dict["result"];
                            try { Services.Logger.Debug($"TryLoadQos: dict['result'] runtime type = {res?.GetType().FullName ?? "<null>"}"); } catch { }
                            // result may be a JsonElement array, an IEnumerable, or other
                            if (res is System.Text.Json.JsonElement resJe && resJe.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                QosProfiles.Clear();
                                foreach (var el in resJe.EnumerateArray())
                                {
                                    if (el.ValueKind == System.Text.Json.JsonValueKind.String) { var s = el.GetString(); if (!string.IsNullOrWhiteSpace(s)) QosProfiles.Add(s); }
                                    else if (el.ValueKind == System.Text.Json.JsonValueKind.Object)
                                    {
                                        // object could be { name: {...} } so take first property name
                                        foreach (var p in el.EnumerateObject()) { var key = p.Name; if (!string.IsNullOrWhiteSpace(key)) { QosProfiles.Add(key); break; } }
                                    }
                                }
                                filled = QosProfiles.Count > 0;
                                Services.Logger.Debug($"TryLoadQos: processed JsonElement result entries, count={QosProfiles.Count}");
                            }
                            else if (res is System.Collections.IEnumerable rlist)
                            {
                                QosProfiles.Clear();
                                foreach (var it in rlist)
                                {
                                    // accept string or JsonElement string
                                    if (it is string s) { if (!string.IsNullOrWhiteSpace(s)) QosProfiles.Add(s); }
                                    else if (it is System.Text.Json.JsonElement jel && jel.ValueKind == System.Text.Json.JsonValueKind.String)
                                    {
                                        var s2 = jel.GetString(); if (!string.IsNullOrWhiteSpace(s2)) QosProfiles.Add(s2);
                                    }
                                    else if (it is System.Collections.IDictionary idit)
                                    {
                                        // if element is a single-entry map { name: {...} } where key is qos name
                                        try
                                        {
                                            foreach (System.Collections.DictionaryEntry kv in idit)
                                            {
                                                var key = kv.Key?.ToString();
                                                if (!string.IsNullOrWhiteSpace(key)) { QosProfiles.Add(key); break; }
                                            }
                                        }
                                        catch { }
                                    }
                                    else
                                    {
                                        // skip other types to avoid adding detail objects
                                    }
                                }
                                filled = QosProfiles.Count > 0;
                                Services.Logger.Debug($"TryLoadQos: processed IEnumerable result entries, count={QosProfiles.Count}");
                            }
                        }
                        catch (Exception ex) { Services.Logger.Debug($"TryLoadQos: exception parsing dict['result']: {ex.Message}"); }
                    }

                    // IDictionary 'detail' 처리
                    if (getRsp.Data is System.Collections.IDictionary dict2 && dict2.Contains("detail"))
                    {
                        try
                        {
                            QosDetails.Clear();
                            var det = dict2["detail"];
                            // det may be a JsonElement (from codec), a dictionary, or an enumerable of per-name objects
                            if (det is System.Text.Json.JsonElement detJe)
                            {
                                // handle JsonElement array/object similarly to JsonElement branch above
                                if (detJe.ValueKind == System.Text.Json.JsonValueKind.Object)
                                {
                                    foreach (var prop in detJe.EnumerateObject())
                                    {
                                        try
                                        {
                                            var pretty = System.Text.Json.JsonSerializer.Serialize(prop.Value, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                                            QosDetails[prop.Name] = pretty;
                                        }
                                        catch { }
                                    }
                                }
                                else if (detJe.ValueKind == System.Text.Json.JsonValueKind.Array)
                                {
                                    foreach (var item in detJe.EnumerateArray())
                                    {
                                        if (item.ValueKind == System.Text.Json.JsonValueKind.Object)
                                        {
                                            foreach (var prop in item.EnumerateObject())
                                            {
                                                try
                                                {
                                                    var pretty = System.Text.Json.JsonSerializer.Serialize(prop.Value, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                                                    QosDetails[prop.Name] = pretty;
                                                }
                                                catch { }
                                            }
                                        }
                                    }
                                }
                            }
                            else if (det is System.Collections.IDictionary dd)
                            {
                                foreach (System.Collections.DictionaryEntry kv in dd)
                                {
                                    try { QosDetails[kv.Key?.ToString() ?? ""] = System.Text.Json.JsonSerializer.Serialize(kv.Value, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }); } catch { }
                                }
                            }
                            else if (det is System.Collections.IEnumerable detEnum && !(det is string))
                            {
                                // iterate array-like detail: elements may be IDictionary or JsonElement-like structures
                                foreach (var item in detEnum)
                                {
                                    if (item is System.Collections.IDictionary itemDict)
                                    {
                                        foreach (System.Collections.DictionaryEntry kv in itemDict)
                                        {
                                            try { QosDetails[kv.Key?.ToString() ?? ""] = System.Text.Json.JsonSerializer.Serialize(kv.Value, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }); } catch { }
                                        }
                                    }
                                    else
                                    {
                                        try
                                        {
                                            // fallback: try serialize item and inspect as JsonDocument
                                            var raw = System.Text.Json.JsonSerializer.Serialize(item);
                                            using var doc = System.Text.Json.JsonDocument.Parse(raw);
                                            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                                            {
                                                foreach (var prop in doc.RootElement.EnumerateObject())
                                                {
                                                    try { QosDetails[prop.Name] = System.Text.Json.JsonSerializer.Serialize(prop.Value, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }); } catch { }
                                                }
                                            }
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    Log($"get qos parse error: {ex.Message}");
                }

                if (filled)
                {
                    SelectedQosProfile = QosProfiles.Count > 0 ? QosProfiles[0] : null;
                    // Ensure UI changes happen on the UI thread
                    try
                    {
                        var app = System.Windows.Application.Current;
                        if (app != null && !app.Dispatcher.CheckAccess())
                        {
                            app.Dispatcher.Invoke(() => { IsQosLoaded = true; });
                        }
                        else
                        {
                            IsQosLoaded = true;
                        }
                    }
                    catch
                    {
                        // fallback
                        IsQosLoaded = true;
                    }
                    Services.Logger.Debug($"TryLoadQos: filled=true QosProfiles.count={QosProfiles.Count}");
                    return true;
                }

                Services.Logger.Debug($"TryLoadQos: filled=false QosProfiles.count={QosProfiles.Count}");

                return false;
            }
            catch (System.OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Log($"TryLoadQosFromServerAsync error: {ex.Message}");
                return false;
            }
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
            string json;
            try
            {
                if (payload is string s) json = s;
                else if (payload is byte[] b) json = "[CBOR] " + Convert.ToBase64String(b);
                else json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
            }
            catch
            {
                json = System.Text.Json.JsonSerializer.Serialize(payload);
            }
            var prettyOut = json;
            try { prettyOut = System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Deserialize<object>(json), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }); } catch { }
            var rawTruncated = Services.Logger.TruncateUtf8(json);
            Traffic.Add(new TrafficItem
            {
                Header = $"[{_clock.Now():HH:mm:ss}] OUT {action}",
                Raw = rawTruncated,
                Pretty = prettyOut,
                IsInbound = false
            });
            try { UpdateTrafficText(); } catch { }
            // Emit concise TRACE entry (no raw payload)
            Services.Logger.Trace($"OUT {action} len={json.Length}");
        }

        private void AddInTraffic(string action, object? payload)
        {
            string json;
            try
            {
                if (payload is string s) json = s;
                else if (payload is byte[] b) json = "[CBOR] " + Convert.ToBase64String(b);
                else json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
            }
            catch
            {
                json = System.Text.Json.JsonSerializer.Serialize(payload);
            }
            var prettyIn = json;
            try { prettyIn = System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Deserialize<object>(json), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }); } catch { }
            var rawTruncated = Services.Logger.TruncateUtf8(json);
            Traffic.Add(new TrafficItem
            {
                Header = $"[{_clock.Now():HH:mm:ss}] IN {action}",
                Raw = rawTruncated,
                Pretty = prettyIn,
                IsInbound = true
            });
            try { UpdateTrafficText(); } catch { }
            Services.Logger.Trace($"IN {action} len={json.Length}");
        }

        private void UpdateTrafficText()
        {
            try
            {
                var lines = new System.Text.StringBuilder();
                foreach (var t in Traffic)
                {
                    lines.AppendLine(t.Header);
                    lines.AppendLine(t.Raw);
                    lines.AppendLine();
                }
                TrafficText = lines.ToString();
            }
            catch { }
        }

    /// <summary>
    /// 뷰에서 폴더 선택 동작을 위임받아 호출할 수 있는 훅입니다.
    /// View(MainWindow)에서 Windows 전용 FolderBrowserDialog를 처리하고 이 Action을 호출하도록 설정합니다.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public System.Action? BrowseConfigAction { get; set; }

        /// <summary>
        /// 외부(뷰)에서 선택한 경로를 ViewModel에 적용하고 구성 재로딩을 수행합니다.
        /// </summary>
        /// <param name="path">선택된 구성 폴더 경로</param>
        public void ApplyConfigRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            ConfigRoot = path;
            Log($"Config set: {ConfigRoot}");
            ReloadConfig();
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

        /// <summary>
        /// XML 파일에서 qos_profile 요소를 읽어 QosProfiles/QosDetails를 대체합니다.
        /// 각 qos_profile의 name 속성을 리스트 항목으로 사용하고, 요소 전체(XML 텍스트)를 상세로 저장합니다.
        /// </summary>
        private void LoadQosFromFile()
        {
            try
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Load QoS XML",
                    Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                    CheckFileExists = true
                };
                var res = ofd.ShowDialog();
                if (res != true) return;
                var path = ofd.FileName;

                var doc = XDocument.Load(path);
                var root = doc.Root;
                var ns = root?.GetDefaultNamespace() ?? XNamespace.None;

                var profiles = doc.Descendants(ns + "qos_profile").ToList();
                if (profiles.Count == 0)
                {
                    Log("No qos_profile elements found in file");
                    return;
                }

                QosProfiles.Clear();
                QosDetails.Clear();
                int i = 0;
                foreach (var p in profiles)
                {
                    i++;
                    var name = p.Attribute("name")?.Value;
                    if (string.IsNullOrWhiteSpace(name)) name = $"profile#{i}";
                    QosProfiles.Add(name);
                    // store the whole element as XML text (preserve formatting produced by ToString)
                    QosDetails[name] = p.ToString();
                }

                SelectedQosProfile = QosProfiles.Count > 0 ? QosProfiles[0] : null;
                IsQosLoaded = true;
                Log($"Loaded {QosProfiles.Count} QoS profiles from file: {path}");
            }
            catch (Exception ex)
            {
                Log($"LoadQoSFromFile error: {ex.Message}");
            }
        }
    }
}
