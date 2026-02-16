using UnityEngine;

public class MediumHealth : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;
    public float currentHealth;

    [Header("Ghost Drain Settings")]
    public float drainRadius = 10f;
    public float maxDrainPerSecond = 20f;

    private Transform ghost;

    private void Start()
    {
        currentHealth = maxHealth;

        GameObject ghostObj = GameObject.FindGameObjectWithTag("Ghost");
        if (ghostObj != null)
            ghost = ghostObj.transform;
    }

    private void Update()
    {
        if (ghost == null) return;

        float distance = Vector3.Distance(transform.position, ghost.position);

        if (distance < drainRadius)
        {
            float proximityPercent = 1f - (distance / drainRadius);
            float drainAmount = maxDrainPerSecond * proximityPercent * Time.deltaTime;

            currentHealth -= drainAmount;
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

            if (currentHealth <= 0f)
            {
                Die();
            }
        }
    }

    private void Die()
    {
        Debug.Log("Medium has been consumed ??");
        // Add death logic here
    }
}