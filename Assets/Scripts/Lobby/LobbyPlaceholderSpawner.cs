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

            if (!_byUserId.TryGetValue(userId, out var go) || go == null)
            {
                var pose = GetSlotPose(i);
                go = Instantiate(lobbyPlaceholderPrefab, pose.position, pose.rotation, transform);
                go.name = "LobbyPlaceholder_" + ShortId(userId);
                ApplyDeterministicModel(go, userId);
                _byUserId[userId] = go;
            }

            var target = GetSlotPose(i);
            go.transform.SetPositionAndRotation(target.position, target.rotation);
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
            _byUserId.Remove(toRemove[i]);
        }
    }

    public void ClearAll()
    {
        foreach (var kv in _byUserId)
        {
            if (kv.Value) Destroy(kv.Value);
        }
        _byUserId.Clear();
    }

    private int GetMaxSlots()
    {
        if (slotAnchors != null && slotAnchors.Length > 0) return slotAnchors.Length;
        return 4;
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
