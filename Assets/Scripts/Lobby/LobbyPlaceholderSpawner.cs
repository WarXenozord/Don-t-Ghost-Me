using System.Collections.Generic;
using UnityEngine;

public class LobbyPlaceholderSpawner : MonoBehaviour
{
    [Header("Prefab")]
    public GameObject lobbyPlaceholderPrefab;

    [Header("Slots (1..N)")]
    public Transform[] slotAnchors;

    [Header("Fallback")]
    public Vector3 fallbackStart = new Vector3(-3f, 0f, 0f);
    public float fallbackSpacing = 2f;

    private readonly Dictionary<string, GameObject> _byUserId = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, int> _slotByUserId = new Dictionary<string, int>();
    private struct SlotPose
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    public void SyncPlayers(List<string> orderedUserIds)
    {
        if (orderedUserIds == null) return;
        if (lobbyPlaceholderPrefab == null) return;

        var maxSlots = GetMaxSlots();
        var allowed = new HashSet<string>();

        for (var i = 0; i < orderedUserIds.Count && i < maxSlots; i++)
        {
            var userId = orderedUserIds[i];
            if (string.IsNullOrEmpty(userId)) continue;
            allowed.Add(userId);
            SpawnOrMoveUser(userId, i);
        }

        var toRemove = new List<string>();
        foreach (var kv in _byUserId)
        {
            if (!allowed.Contains(kv.Key))
            {
                if (kv.Value) Destroy(kv.Value);
                toRemove.Add(kv.Key);
            }
        }

        for (var i = 0; i < toRemove.Count; i++)
        {
            _slotByUserId.Remove(toRemove[i]);
            _byUserId.Remove(toRemove[i]);
        }
    }

    public void SpawnOrMoveUser(string userId, int slotIndex)
    {
        if (string.IsNullOrEmpty(userId)) return;
        if (lobbyPlaceholderPrefab == null) return;

        if (!_byUserId.TryGetValue(userId, out var go) || go == null)
        {
            var pose = GetSlotPose(slotIndex);
            go = Instantiate(lobbyPlaceholderPrefab, pose.position, pose.rotation, transform);
            go.name = "LobbyPlaceholder_" + ShortId(userId);
            ApplyDeterministicModel(go, userId);
            _byUserId[userId] = go;
        }

        _slotByUserId[userId] = slotIndex;
        var target = GetSlotPose(slotIndex);
        go.transform.SetPositionAndRotation(target.position, target.rotation);
    }

    public void RemoveUser(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return;
        _slotByUserId.Remove(userId);
        if (_byUserId.TryGetValue(userId, out var go))
        {
            if (go) Destroy(go);
            _byUserId.Remove(userId);
        }
    }

    public void ClearAll()
    {
        foreach (var kv in _byUserId)
        {
            if (kv.Value) Destroy(kv.Value);
        }
        _byUserId.Clear();
        _slotByUserId.Clear();
    }

    private int GetMaxSlots()
    {
        var configured = slotAnchors != null ? slotAnchors.Length : 0;
        return Mathf.Max(4, configured);
    }

    private SlotPose GetSlotPose(int slotIndex)
    {
        if (slotAnchors != null && slotIndex >= 0 && slotIndex < slotAnchors.Length && slotAnchors[slotIndex] != null)
        {
            var t = slotAnchors[slotIndex];
            return new SlotPose { position = t.position, rotation = t.rotation };
        }

        return new SlotPose
        {
            position = fallbackStart + new Vector3(fallbackSpacing * slotIndex, 0f, 0f),
            rotation = Quaternion.identity
        };
    }

    private static string ShortId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "------";
        return id.Length <= 6 ? id : id.Substring(0, 6);
    }

    private static void ApplyDeterministicModel(GameObject go, string userId)
    {
        if (go == null || string.IsNullOrEmpty(userId)) return;

        var randomizer = go.GetComponent<CharacterModelRandomizer>();
        if (randomizer == null) randomizer = go.GetComponentInChildren<CharacterModelRandomizer>(true);
        if (randomizer == null) return;

        randomizer.SpawnModelFromUserId(userId);
    }
}
