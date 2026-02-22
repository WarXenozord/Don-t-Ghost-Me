using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class GhostEnergy : MonoBehaviour
{
    [Header("Energy")]
    public float maxHealth = 100f;
    public float currentHealth;
    private Image energyBarFill;
    public GameObject energyBar;

    [Header("Ghost Drain Settings")]
    public float drainRadius = 10f;
    public float maxDrainPerMedium = 15f; // per medium per second

    [Header("Low Energy Burst Kill")]
    public float lowEnergyKillThreshold = 5f;
    public float lowEnergyKillRadius = 4f;
    public float lowEnergyKillCooldown = 1.5f;
    public AudioSource lowEnergyKillAudioSource;
    public AudioClip lowEnergyKillClip;
    [Header("Low Energy Loop SFX (Local)")]
    public float lowEnergyLoopThreshold = 20f;
    public AudioSource lowEnergyLoopSource;
    public AudioClip lowEnergyLoopClip;

    [Header("Regen Settings")]
    public float regenPerSecond = 10f;
    public float regenDelay = 2f;

    private float timeSinceLastDrain = 0f;
    private float _nextLowEnergyKillAt;
    private bool _warnedHostAuthority;
    private bool _lowEnergyLoopPlaying;

    private NakamaConnection _conn;
    private GhostSpawner _ghostSpawner;
    private PlayerSpawnManager _playerSpawner;
    private readonly List<PlayerSpawnManager.SpawnedPlayerInfo> _spawnedPlayers = new List<PlayerSpawnManager.SpawnedPlayerInfo>();

    private void Start()
    {
        _conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
        _ghostSpawner = GhostSpawner.Instance != null ? GhostSpawner.Instance : FindObjectOfType<GhostSpawner>();
        _playerSpawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();

        var canvasGo = GameObject.FindGameObjectWithTag("Canvas");
        if (energyBar != null && canvasGo != null)
        {
            var canvas = canvasGo.transform;
            var child = Instantiate(energyBar).transform;
            child.SetParent(canvas, false);
            if (child.childCount > 0)
            {
                energyBarFill = child.GetChild(0).GetComponent<Image>();
            }
        }

        currentHealth = maxHealth;
        ConfigureLowEnergyLoopSource();
        UpdateHealthBar();
    }

    private void Update()
    {
        UpdateHealthBar();
        float drainAmount = DrainFromNearbyMediums();

        if (drainAmount > 0f)
        {
            // Ghost is being drained
            currentHealth -= drainAmount;
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

            timeSinceLastDrain = 0f;
        }
        else
        {
            // No drain happening
            timeSinceLastDrain += Time.deltaTime;

            if (timeSinceLastDrain >= regenDelay)
            {
                Regenerate();
            }
        }

        if (currentHealth <= 0f)
        {
            Die();
        }

        if (currentHealth <= lowEnergyKillThreshold)
        {
            TryLowEnergyBurstKill();
        }

        UpdateLowEnergyLoopSfx();
    }

    private float DrainFromNearbyMediums()
    {
        float totalDrain = 0f;
        ResolveRefs();

        if (_playerSpawner != null)
        {
            _playerSpawner.FillSpawnedPlayers(_spawnedPlayers);
            for (var i = 0; i < _spawnedPlayers.Count; i++)
            {
                var info = _spawnedPlayers[i];
                var go = info.root;
                if (go == null) continue;

                // Drain only from alive mediums, ignore ghosts.
                if (go.GetComponentInChildren<GhostController>(true) != null) continue;
                var medium = go.GetComponentInChildren<MediumController>(true);
                var target = medium != null ? medium.transform : go.transform;
                if (target == null) continue;

                var distance = Vector3.Distance(transform.position, target.position);
                if (distance >= drainRadius) continue;

                var proximityPercent = 1f - (distance / drainRadius);
                var drain = maxDrainPerMedium * proximityPercent * Time.deltaTime;
                totalDrain += drain;
            }
        }
        else
        {
            // Fallback path if spawner is unavailable.
            var mediums = GameObject.FindGameObjectsWithTag("Medium");
            for (var i = 0; i < mediums.Length; i++)
            {
                var medium = mediums[i];
                if (medium == null) continue;
                var distance = Vector3.Distance(transform.position, medium.transform.position);
                if (distance >= drainRadius) continue;

                var proximityPercent = 1f - (distance / drainRadius);
                var drain = maxDrainPerMedium * proximityPercent * Time.deltaTime;
                totalDrain += drain;
            }
        }

        return totalDrain;
    }

    private void Regenerate()
    {
        currentHealth += regenPerSecond * Time.deltaTime;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
    }

    private void Die()
    {
        Debug.Log("Ghost has been weakened by the living!");
        // Add death logic here
    }

    private void TryLowEnergyBurstKill()
    {
        if (Time.time < _nextLowEnergyKillAt) return;
        _nextLowEnergyKillAt = Time.time + Mathf.Max(0.1f, lowEnergyKillCooldown);

        ResolveRefs();
        if (_ghostSpawner == null || _playerSpawner == null) return;

        var killed = 0;
        _playerSpawner.FillSpawnedPlayers(_spawnedPlayers);
        for (var i = 0; i < _spawnedPlayers.Count; i++)
        {
            var info = _spawnedPlayers[i];
            if (string.IsNullOrEmpty(info.userId)) continue;

            var go = info.root;
            if (go == null) continue;
            if (go.GetComponentInChildren<GhostController>(true) != null) continue; // skip ghosts

            var medium = go.GetComponentInChildren<MediumController>(true);
            var target = medium != null ? medium.transform : go.transform;
            if (target == null) continue;

            var dist = Vector3.Distance(transform.position, target.position);
            if (dist > lowEnergyKillRadius) continue;

            if (_ghostSpawner.RequestKillMediumAndSpawnGhost(info.userId, target.position, target.eulerAngles.y))
            {
                killed++;
            }
        }

        if (killed <= 0 && !_warnedHostAuthority)
        {
            _warnedHostAuthority = true;
            if (_conn != null && !_conn.IsCurrentPlayerMatchCreator)
            {
                Debug.Log("[GhostEnergy] Low-energy burst attempted on non-host. Host authority prevented local kill.");
            }
        }

        if (killed > 0)
        {
            if (_ghostSpawner != null)
            {
                _ghostSpawner.RequestLowEnergyKillFx(transform.position);
            }
            else
            {
                PlayLowEnergyKillSfx();
            }
        }
    }

    private void PlayLowEnergyKillSfx()
    {
        if (lowEnergyKillClip == null) return;
        if (lowEnergyKillAudioSource != null)
        {
            lowEnergyKillAudioSource.PlayOneShot(lowEnergyKillClip);
            return;
        }

        AudioSource.PlayClipAtPoint(lowEnergyKillClip, transform.position, 1f);
    }

    private void ResolveRefs()
    {
        if (_conn == null) _conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
        if (_ghostSpawner == null) _ghostSpawner = GhostSpawner.Instance != null ? GhostSpawner.Instance : FindObjectOfType<GhostSpawner>();
        if (_playerSpawner == null) _playerSpawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
    }

    void UpdateHealthBar()
    {
        // Update the fill amount based on the health ratio
        if (energyBarFill != null)
        {
            energyBarFill.fillAmount = maxHealth > 0f ? currentHealth / maxHealth : 0f;
        }
        // If using a Slider: healthSlider.value = currentHealth / maxHealth;
    }

    private void ConfigureLowEnergyLoopSource()
    {
        if (lowEnergyLoopSource == null)
        {
            lowEnergyLoopSource = GetComponent<AudioSource>();
        }

        if (lowEnergyLoopSource == null || lowEnergyLoopClip == null) return;

        lowEnergyLoopSource.clip = lowEnergyLoopClip;
        lowEnergyLoopSource.loop = true;
        lowEnergyLoopSource.playOnAwake = false;
    }

    private void UpdateLowEnergyLoopSfx()
    {
        if (lowEnergyLoopSource == null || lowEnergyLoopClip == null) return;

        var shouldPlay = currentHealth < lowEnergyLoopThreshold;
        if (shouldPlay && !_lowEnergyLoopPlaying)
        {
            if (lowEnergyLoopSource.clip != lowEnergyLoopClip)
            {
                lowEnergyLoopSource.clip = lowEnergyLoopClip;
            }
            lowEnergyLoopSource.loop = true;
            lowEnergyLoopSource.Play();
            _lowEnergyLoopPlaying = true;
        }
        else if (!shouldPlay && _lowEnergyLoopPlaying)
        {
            lowEnergyLoopSource.Stop();
            _lowEnergyLoopPlaying = false;
        }
    }
}
