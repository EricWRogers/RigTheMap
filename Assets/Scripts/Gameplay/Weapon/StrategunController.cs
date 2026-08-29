using UnityEngine;

public class StrategunController : MonoBehaviour
{
    [Header("Placement Settings")]
    public GameObject gameObjectToPlace;
    public float maxPlaceDistance = 15f;
    public LayerMask placementLayerMask;

    [Header("Preview Settings")]
    public Material previewMat;
    
    private GameObject previewGhost;
    private Camera cam;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        cam = Camera.main;
        CreatePreview();
    }

    // Update is called once per frame
    void Update()
    {
        HandlePlacement();
    }

    void HandlePlacement()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)); 
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, maxPlaceDistance, placementLayerMask))
        {
            if (!previewGhost.activeSelf) previewGhost.SetActive(true);

            Vector3 spawnPos = hit.point;
            Quaternion spawnRot = Quaternion.FromToRotation(Vector3.up, hit.normal);

            previewGhost.transform.position = spawnPos;
            previewGhost.transform.rotation = spawnRot;

            if (Input.GetMouseButtonDown(0))
            {
                Instantiate(gameObjectToPlace, spawnPos, spawnRot);
            }
        }
        else if (previewGhost.activeSelf)
        {
            previewGhost.SetActive(false); // Hide if the player isn't looking at a surface
        }
    }

    void CreatePreview()
    {
        if (gameObjectToPlace == null) return;

        previewGhost = Instantiate(gameObjectToPlace);

        if (previewGhost.TryGetComponent<Collider>(out Collider coll))
        {
            Destroy(coll);
        }

        if (previewMat != null)
        {
            MeshRenderer[] renderers = previewGhost.GetComponentsInChildren<MeshRenderer>();
            foreach (MeshRenderer renderer in renderers)
            {
                renderer.material = previewMat;
            }
        }
        previewGhost.SetActive(false); //we dont want to see unless looking at a valid surface
    }
}
