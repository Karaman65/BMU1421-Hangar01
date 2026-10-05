using UnityEngine;

public class PervaneDondur : MonoBehaviour
{
    [SerializeField] private float donusHizi = 1200f;
    [SerializeField] private Vector3 donusEkseni = Vector3.forward;

    private void Update()
    {
        transform.Rotate(donusEkseni, donusHizi * Time.deltaTime, Space.Self);
    }
}
