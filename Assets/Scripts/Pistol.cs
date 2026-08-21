using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(AudioSource))]
public class Pistol : MonoBehaviour
{
    public GameObject bulletPrefab;
    public Transform firePoint;
    public float bulletSpeed = 20f;
    public float bulletLifetime = 5f; // seconds before bullet is destroyed

    public AudioClip clip;
    private AudioSource source;
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;

    private void Start()
    {
        source = GetComponent<AudioSource>();
        
    }


    public void FireBullet()
    {
        GameObject bullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        Rigidbody rb = bullet.GetComponent<Rigidbody>();

        source.PlayOneShot(clip);

        if (rb != null)
        {
            rb.linearVelocity = firePoint.forward * bulletSpeed;
        }

        // Destroy the bullet after a set time
        Destroy(bullet, bulletLifetime);
    }
}