using System;
using System.Collections.ObjectModel;
using Microsoft.Win32;
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
        public string ConfigRoot { get => _configRoot; set => SetField(ref _configRoot, value); }

        // DDS
        private string _domainId = "0";
        public string DomainId { get => _domainId; set => SetField(ref _domainId, value); }

        public ObservableCollection<string> QosProfiles { get; } = new();
        private string? _selectedQosProfile;
        public string? SelectedQosProfile { get => _selectedQosProfile; set => SetField(ref _selectedQosProfile, value); }

        public ObservableCollection<string> TypeNames { get; } = new();
        private string? _selectedType;
        public string? SelectedType { get => _selectedType; set => SetField(ref _selectedType, value); }

        private string _topic = "";
        public string Topic { get => _topic; set => SetField(ref _topic, value); }

        private string _payload = "{}";
        public string Payload { get => _payload; set => SetField(ref _payload, value); }

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

        private readonly ClockService _clock;

        public MainViewModel(string configRoot, ClockService clock)
        {
            _clock = clock;
            _configRoot = configRoot;

            ConnectCommand = new RelayCommand(() => Log($"Connect to {Address}:{Port} as {SelectedRole}"));
            DisconnectCommand = new RelayCommand(() => Log("Disconnect"));
            BrowseConfigCommand = new RelayCommand(BrowseConfig);
            ReloadConfigCommand = new RelayCommand(ReloadConfig);
            CreateParticipantCommand = new RelayCommand(() => Log($"Create Participant (domain={DomainId}, qos={SelectedQosProfile})"));
            ClearDdsCommand = new RelayCommand(() => Log("Clear DDS"));
            OpenFormCommand = new RelayCommand(() => Log("Open dynamic form (TODO)"));
            FillSampleCommand = new RelayCommand(() => Payload = "{\n  \"sample\": true\n}");
            CreateWriterCommand = new RelayCommand(() => Log($"Create Writer on topic '{Topic}'"));
            CreateReaderCommand = new RelayCommand(() => Log($"Create Reader on topic '{Topic}'"));
            PublishCommand = new RelayCommand(() => Log($"Publish to '{Topic}' payload bytes={Payload.Length}"));
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
            var dlg = new OpenFileDialog
            {
                Title = "Pick any file inside your config directory",
                Filter = "All files|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                var dir = System.IO.Path.GetDirectoryName(dlg.FileName)!;
                ConfigRoot = dir;
                Log($"Config set: {ConfigRoot}");
                ReloadConfig();
            }
        }

        private void ReloadConfig()
        {
            // TODO: Replace with real loader (reads config/qos|topics|generated)
            QosProfiles.Clear();
            QosProfiles.Add("Lib::Profile_A");
            QosProfiles.Add("Lib::Profile_B");

            TypeNames.Clear();
            TypeNames.Add("C_SampleType");
            TypeNames.Add("C_AnotherType");

            Log("Config reloaded");
        }
    }
}
