using System;
using System.Collections.ObjectModel;
using Microsoft.Win32;
using System.Windows.Forms;
using Agent.UI.Wpf.Services;

namespace Agent.UI.Wpf.ViewModels
{
    public sealed class MainViewModel : ObservableObject
    {
        // Connection
        public string[] Roles { get; } = new[] { "client", "server" };
        private string _selectedRole = "client";
        public string SelectedRole { get => _selectedRole; set => SetField(ref _selectedRole, value); }

        private string _address = "127.0.0.1";
        public string Address { get => _address; set => SetField(ref _address, value); }

        private string _port = "9000";
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

        private readonly ClockService _clock;

        public MainViewModel(string configRoot, ClockService clock)
        {
            _clock = clock;
            // Default config root: if not provided, assume 'config' folder next to exe
            _configRoot = string.IsNullOrWhiteSpace(configRoot)
                ? System.IO.Path.Combine(AppContext.BaseDirectory, "config")
                : configRoot;

            // Wire service logging to ViewModel Log
            Agent.UI.Wpf.Services.ConfigService.LogAction = (s) => Log(s);

            ConnectCommand = new RelayCommand(() => Log($"Connect to {Address}:{Port} as {SelectedRole}"));
            DisconnectCommand = new RelayCommand(() => Log("Disconnect"));
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

        private void Log(string msg)
        {
            Logs.Add($"[{_clock.Now():HH:mm:ss}] {msg}");
            Status = msg;
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
