using FishNet.Component.Spawning;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using LightMMORPG.Combat;
using LightMMORPG.Player;
using UnityEngine;

namespace LightMMORPG.Networking
{
    /// <summary>
    /// Training mode: a private local match. <see cref="NetworkBootstrap"/> starts a host bound to the loopback address on
    /// <see cref="Port"/> (other computers cannot reach it, a second local connection is kicked) and this component spawns
    /// a <see cref="TrainingDummy"/> fighter a few metres in front of the player once the player has spawned.
    /// </summary>
    public sealed class TrainingSession : MonoBehaviour
    {
        public const ushort Port = NetworkBootstrap.DefaultPort + 1;
        public const string DummyName = "Training Dummy";
        private const float DummyDistance = 5f;

        private NetworkManager _nm;
        private PlayerSpawner _spawner;
        private bool _spawned;

        public static TrainingSession Begin(NetworkManager nm)
        {
            var s = nm.gameObject.AddComponent<TrainingSession>();
            s._nm = nm;
            s._spawner = nm.GetComponent<PlayerSpawner>();
            if (s._spawner != null) s._spawner.OnSpawned += s.OnPlayerSpawned;
            nm.ServerManager.OnRemoteConnectionState += s.OnRemoteConnection;
            return s;
        }

        private void OnDestroy()
        {
            if (_spawner != null) _spawner.OnSpawned -= OnPlayerSpawned;
            if (_nm != null) _nm.ServerManager.OnRemoteConnectionState -= OnRemoteConnection;
        }

        private void OnRemoteConnection(NetworkConnection conn, RemoteConnectionStateArgs a)
        {
            if (a.ConnectionState != RemoteConnectionState.Started || _nm.ServerManager.Clients.Count <= 1) return;
            Debug.Log("[Training] Refusing extra connection " + conn.ClientId + " (training is single-player)");
            conn.Disconnect(true);
        }

        private void OnPlayerSpawned(NetworkObject player)
        {
            if (_spawned || player == null) return;
            _spawned = true;
            Vector3 fwd = player.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 home = player.transform.position + fwd * DummyDistance;
            Quaternion rot = Quaternion.LookRotation(-fwd, Vector3.up);

            var dummy = _nm.GetPooledInstantiated(player.PrefabId, player.SpawnableCollectionId, home, rot, true);
            if (dummy == null) { Debug.LogWarning("[Training] Could not create the dummy"); return; }
            dummy.name = DummyName;
            _nm.ServerManager.Spawn(dummy); // no owner: the server drives it
            var id = dummy.GetComponent<PlayerIdentity>();
            if (id != null) id.DisplayName.Value = DummyName;
            TrainingDummy.Attach(dummy.gameObject, home);
            Debug.Log("[Training] Dummy spawned at (" + home.x.ToString("F1") + ", " + home.z.ToString("F1") + ")");
        }
    }
}
