using UnityEngine;
using UnityEngine.UI;

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

    [Header("Regen Settings")]
    public float regenPerSecond = 10f;
    public float regenDelay = 2f;

    private float timeSinceLastDrain = 0f;

    private GameObject[] mediums;

    private void Start()
    {
        Transform canvas = GameObject.FindGameObjectWithTag("Canvas").GetComponent<Transform>();
        Transform child = Instantiate(energyBar).GetComponent<Transform>();
        child.transform.SetParent(canvas);


        currentHealth = maxHealth;
        energyBarFill= child.GetChild(0).GetComponent<Image>();
        UpdateHealthBar();
        // Cache all mediums at start
        mediums = GameObject.FindGameObjectsWithTag("Medium");
    }

    private void Update()
    {
        UpdateHealthBar();
        float drainAmount = DrainFromMediums();

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
    }
    private float DrainFromMediums()
    {
        float totalDrain = 0f;

        GameObject[] mediums = GameObject.FindGameObjectsWithTag("Medium");

        foreach (GameObject medium in mediums)
        {
            float distance = Vector3.Distance(
                transform.position,
                medium.transform.position
            );

            if (distance < drainRadius)
            {
                float proximityPercent = 1f - (distance / drainRadius);

                float drain =
                    maxDrainPerMedium *
                    proximityPercent *
                    Time.deltaTime;

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
    void UpdateHealthBar()
    {
        // Update the fill amount based on the health ratio
        energyBarFill.fillAmount = currentHealth / maxHealth;
        // If using a Slider: healthSlider.value = currentHealth / maxHealth;
    }
}