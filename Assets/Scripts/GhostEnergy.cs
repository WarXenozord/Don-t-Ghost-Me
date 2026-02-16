using UnityEngine;
using UnityEngine.UI;

public class GhostEnergy : MonoBehaviour
{
    [Header("Energy")]
    public float maxHealth = 100f;
    public float currentHealth;
     public Image healthBarFill;

    [Header("Ghost Drain Settings")]
    public float drainRadius = 10f;
    public float maxDrainPerMedium = 15f; // per medium per second

    private GameObject[] mediums;

    private void Start()
    {
        currentHealth = maxHealth;
        UpdateHealthBar();
        // Cache all mediums at start
        mediums = GameObject.FindGameObjectsWithTag("Medium");
    }

    private void Update()
    {
        UpdateHealthBar();
        DrainFromMediums();
    }
    private void DrainFromMediums()
    {
        float totalDrainThisFrame = 0f;

        foreach (GameObject medium in mediums)
        {
            if (medium == null) continue;

            float distance = Vector3.Distance(
                transform.position,
                medium.transform.position
            );

            if (distance < drainRadius)
            {
                float proximityPercent = 1f - (distance / drainRadius);

                float drainAmount =
                    maxDrainPerMedium *
                    proximityPercent *
                    Time.deltaTime;

                totalDrainThisFrame += drainAmount;
            }
        }

        currentHealth -= totalDrainThisFrame;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        Debug.Log("Ghost has been weakened by the living!");
        // Add death logic here
    }
    void UpdateHealthBar()
    {
        // Update the fill amount based on the health ratio
        healthBarFill.fillAmount = currentHealth / maxHealth;
        // If using a Slider: healthSlider.value = currentHealth / maxHealth;
    }
}