using JetBrains.Annotations;
using UnityEngine;

public class EndLvl : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
     
        void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Body"))
            {
                Debug.Log("Level Complete!");
                // Add your level completion logic here, such as loading the next level or displaying a victory screen.
            }
        }
    }
}
