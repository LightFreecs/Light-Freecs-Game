using System;
using System.Globalization;
using System.Text;
using FishNet.Managing;
using FishNet.Transporting;
using LightMMORPG.Player;
using UnityEngine;

namespace LightMMORPG.Networking
{
    /// <summary>
    /// Starts host / client / server. Sits on the NetworkManager object in Arena_01. What to start comes from the main
    /// menu (<see cref="NetLaunch.Launch"/>) or the command line: <c>-host</c>, <c>-client [ip]</c>, <c>-server</c>, <c>-port N</c>, <c>-netlog</c> (log player positions
    /// every 2 s), <c>-quitafter S</c> (exit after S seconds), <c>-screenshot file.png [-screenshotat S]</c> (one screenshot, default after 8 s). Batch mode / UNITY_SERVER without a flag = dedicated server.
    /// </summary>
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        public const ushort DefaultPort = 7770;
        public const string DefaultAddress = "127.0.0.1";

        public static NetworkBootstrap Instance { get; private set; }
        public NetworkManager Manager { get; private set; }
        public ushort Port { get; private set; } = DefaultPort;
        /// <summary>Last connection status message (shown by the menu).</summary>
        public string Status { get; private set; } = string.Empty;
        public event Action OnStatusChanged;

        /// <summary>What this instance was asked to start (None until then).</summary>
        public NetMode StartedMode { get; private set; }
        /// <summary>Address a client connects to (host/server: 127.0.0.1).</summary>
        public string Address { get; private set; } = DefaultAddress;

        public bool IsRunning => Manager != null && (Manager.IsServerStarted || Manager.IsClientStarted);

        private bool _netLog;
        private float _quitAt = -1f;
        private float _shotAt = -1f;
        private string _shotPath;
        private float _nextLog;

        private void Awake()
        {
            Instance = this;
            Manager = GetComponent<NetworkManager>();
            Application.runInBackground = true;
            if (Application.isBatchMode) Application.targetFrameRate = 60;
        }

        private void Start()
        {
            Manager.ServerManager.OnServerConnectionState += a => SetStatus("Server " + a.ConnectionState);
            Manager.ClientManager.OnClientConnectionState += a => SetStatus("Client " + a.ConnectionState);
            Manager.ServerManager.OnRemoteConnectionState += (conn, a) =>
                Debug.Log("[Net] Remote client " + conn.ClientId + " " + a.ConnectionState);

            string[] args = Environment.GetCommandLineArgs();
            string portArg = Arg(args, "-port");
            if (portArg != null && ushort.TryParse(portArg, out ushort p)) Port = p;
            _netLog = Has(args, "-netlog");
            _shotPath = Arg(args, "-screenshot");
            string shotAt = Arg(args, "-screenshotat");
            if (_shotPath != null) _shotAt = Time.realtimeSinceStartup + (shotAt != null && float.TryParse(shotAt, NumberStyles.Float, CultureInfo.InvariantCulture, out float sa) ? sa : 8f);
            string quit = Arg(args, "-quitafter");
            if (quit != null && float.TryParse(quit, NumberStyles.Float, CultureInfo.InvariantCulture, out float q)) _quitAt = Time.realtimeSinceStartup + q;

            // A request from the main menu wins; otherwise the command line (-host / -client ip / -server / batch = server).
            if (!NetLaunch.TryConsume(out NetMode mode, out string address))
                mode = NetLaunch.FromCommandLine(out address);
            switch (mode)
            {
                case NetMode.Host: StartHost(); break;
                case NetMode.Client: StartClient(address); break;
                case NetMode.Server: StartServerOnly(); break;
                case NetMode.Training: StartTraining(); break;
                default:
                    // Arena opened directly (editor play mode) with nothing to do: go to the menu.
                    Debug.Log("[Net] No launch request: returning to the main menu");
                    NetLaunch.ReturnToMenu();
                    break;
            }
        }

        public void StartHost()
        {
            Debug.Log("[Net] Starting host on port " + Port);
            StartedMode = NetMode.Host;
            SetPort();
            Manager.ServerManager.StartConnection(Port);
            Manager.ClientManager.StartConnection(DefaultAddress, Port);
        }

        /// <summary>Training mode: a host only this computer can reach (loopback), with a practice dummy.</summary>
        public void StartTraining()
        {
            Port = TrainingSession.Port;
            Debug.Log("[Net] Starting training (local only) on port " + Port);
            StartedMode = NetMode.Training;
            SetPort();
            var t = Manager.TransportManager.Transport;
            if (t != null)
            {
                t.SetServerBindAddress("127.0.0.1", IPAddressType.IPv4);
                t.SetServerBindAddress("::1", IPAddressType.IPv6);
            }
            TrainingSession.Begin(Manager);
            Manager.ServerManager.StartConnection(Port);
            Manager.ClientManager.StartConnection(DefaultAddress, Port);
        }

        public void StartClient(string address)
        {
            Debug.Log("[Net] Connecting to " + address + ":" + Port);
            StartedMode = NetMode.Client;
            Address = address;
            SetPort();
            Manager.ClientManager.StartConnection(address, Port);
        }

        public void StartServerOnly()
        {
            Debug.Log("[Net] Starting dedicated server on port " + Port);
            StartedMode = NetMode.Server;
            SetPort();
            Manager.ServerManager.StartConnection(Port);
        }

        public void StopAll()
        {
            if (Manager.IsClientStarted) Manager.ClientManager.StopConnection();
            if (Manager.IsServerStarted) Manager.ServerManager.StopConnection(true);
        }

        private void SetPort()
        {
            var t = Manager.TransportManager.Transport;
            if (t != null) t.SetPort(Port);
        }

        private void SetStatus(string s)
        {
            Status = s;
            Debug.Log("[Net] " + s);
            OnStatusChanged?.Invoke();
        }

        private void Update()
        {
            if (_shotAt > 0f && Time.realtimeSinceStartup >= _shotAt)
            {
                _shotAt = -1f;
                ScreenCapture.CaptureScreenshot(_shotPath);
                Debug.Log("[Net] Screenshot: " + _shotPath);
            }
            if (_quitAt > 0f && Time.realtimeSinceStartup >= _quitAt)
            {
                Debug.Log("[Net] -quitafter reached, quitting");
                _quitAt = -1f;
                StopAll();
                Application.Quit();
            }
            if (_netLog && Time.realtimeSinceStartup >= _nextLog)
            {
                _nextLog = Time.realtimeSinceStartup + 2f;
                LogPlayers();
            }
        }

        private void LogPlayers()
        {
            var sb = new StringBuilder();
            sb.Append("[NetLog] t=").Append(Time.realtimeSinceStartup.ToString("F1", CultureInfo.InvariantCulture))
              .Append(" server=").Append(Manager.IsServerStarted).Append(" client=").Append(Manager.IsClientStarted)
              .Append(" players=").Append(PlayerIdentity.All.Count);
            foreach (var p in PlayerIdentity.All)
            {
                if (p == null) continue;
                Vector3 pos = p.transform.position;
                sb.Append(" | id=").Append(p.ClientId).Append(p.IsOwner ? "(own)" : "")
                  .Append(" ").Append(p.DisplayName.Value)
                  .Append(" pos=(").Append(pos.x.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                  .Append(pos.y.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                  .Append(pos.z.ToString("F2", CultureInfo.InvariantCulture)).Append(')');
                if (p.Motor != null) sb.Append(p.Motor.IsFlying ? " fly" : "").Append(" v=").Append(p.Motor.Velocity.magnitude.ToString("F1", CultureInfo.InvariantCulture));
            }
            Debug.Log(sb.ToString());
        }

        private static bool Has(string[] args, string flag) => Array.IndexOf(args, flag) >= 0;

        private static string Arg(string[] args, string flag)
        {
            int i = Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
