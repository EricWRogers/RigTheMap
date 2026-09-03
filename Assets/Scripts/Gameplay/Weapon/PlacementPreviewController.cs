using Unity.MP_FPS;
using UnityEngine.AddressableAssets;
using UnityEngine;

public class PlacementPreviewController : MonoBehaviour
{
    public Material previewMaterial;
    public float rotationStep = 15f;
    private GameObject previewInstance;
    private int currentPrefabIndex = -1;
    private float currentRotationDegrees;
    private PlayerGhost localPlayer;
    private PredictedPlayerGhost playerData;
    private WeaponData currentWeaponData;
    private WeaponData previewWeaponData;
    private WeaponData requestedWeaponData;
    private int requestedPrefabIndex = -1;
    private int previewRequestVersion;

    public void Initialize(PlayerGhost player)
    {
        localPlayer = player;
    }

    private void ConfigurePreview(GameObject instance)
    {
        foreach(Collider collider in instance.GetComponentsInChildren<Collider>())
            collider.enabled = false;

        foreach(Renderer renderer in instance.GetComponentsInChildren<Renderer>())
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            renderer.receiveShadows = false;

            if(previewMaterial != null)
                renderer.material = previewMaterial;
        }

        foreach(MonoBehaviour script in instance.GetComponentsInChildren<MonoBehaviour>())
            script.enabled = false;
    }

    private async void CreatePreview(WeaponData weaponData, int selectedIndex)
    {
        var previewReference =
            weaponData.PlacementGhostPrefabs[selectedIndex].GhostPrefab;
        int requestVersion = ++previewRequestVersion;

        if (previewReference == null || !previewReference.RuntimeKeyIsValid())
        {
            return;
        }

        var instance = await previewReference.InstantiateAsync().Task;

        if (instance == null)
        {
            return;
        }

        if (requestVersion != previewRequestVersion)
        {
            Addressables.ReleaseInstance(instance);
            return;
        }

        previewInstance = instance;
        currentPrefabIndex = selectedIndex;
        previewWeaponData = weaponData;
        requestedWeaponData = weaponData;
        requestedPrefabIndex = selectedIndex;

        previewInstance.transform.SetParent(
            GhostBridgeBootstrap.Instance.ClientGameObjectHierarchy.transform);

        ConfigurePreview(previewInstance);
        previewInstance.SetActive(false);
    }

    private void DestroyPreview()
    {
        ++previewRequestVersion;

        if (previewInstance != null)
        {
            Addressables.ReleaseInstance(previewInstance);
            previewInstance = null;
        }

        previewWeaponData = null;
        currentPrefabIndex = -1;
        requestedWeaponData = null;
        requestedPrefabIndex = -1;
    }

    private void OnDestroy()
    {
        DestroyPreview();
    }

    // Update is called once per frame
    void Update()
    {
        if (localPlayer == null ||
            localPlayer.GhostGameObject == null ||
            WeaponManager.Instance == null ||
            WeaponManager.Instance.WeaponRegistry == null)
        {
            return;
        }

        playerData = localPlayer.GhostGameObject.ReadGhostComponentData<PredictedPlayerGhost>();
        currentWeaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(playerData.EquippedWeaponID);

        if(currentWeaponData == null || !currentWeaponData.IsPlacementWeapon || currentWeaponData.PlacementGhostPrefabs == null || currentWeaponData.PlacementGhostPrefabs.Count == 0)
        {
            if (previewInstance != null)
                previewInstance.SetActive(false);
            else if (requestedWeaponData != null)
                DestroyPreview();

            return;
        }

        // change prefab mesh when switch building
        


        int selectedIndex = Mathf.Clamp(playerData.SelectedPlacementPrefabIndex, 0, currentWeaponData.PlacementGhostPrefabs.Count - 1);

        if (currentWeaponData != previewWeaponData || selectedIndex != currentPrefabIndex)
        {
            if (currentWeaponData != requestedWeaponData || selectedIndex != requestedPrefabIndex)
            {
                DestroyPreview();
                requestedWeaponData = currentWeaponData;
                requestedPrefabIndex = selectedIndex;
                currentRotationDegrees = ClientInputReaderSystem.PlacementRotationDegrees;
                CreatePreview(currentWeaponData, selectedIndex);
            }

            return;
        }

        if (previewInstance == null)
            return;

        currentRotationDegrees = ClientInputReaderSystem.PlacementRotationDegrees;

        int placementMask = currentWeaponData.PlacementLayerMask.value != 0
        ? currentWeaponData.PlacementLayerMask.value
        : LayerMask.GetMask("Ground", "Default");

        Quaternion aimRotation = Quaternion.Euler(
            playerData.ControllerState.PitchDegrees,
            playerData.ControllerState.YawDegrees,
            0f);

        Vector3 aimDirection = aimRotation * Vector3.forward;

        if(Physics.Raycast(
            localPlayer.CameraTarget.position,
            aimDirection,
            out RaycastHit hit,
            currentWeaponData.HitscanRange,
            placementMask))
        {
            Vector3 position = hit.point + hit.normal * currentWeaponData.PlacementOffset;
            Quaternion surfaceRotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
            Quaternion rotation = surfaceRotation * Quaternion.Euler(0f, currentRotationDegrees, 0f);

            previewInstance.transform.SetPositionAndRotation(position, rotation);

            previewInstance.SetActive(true);
        }
        else
            previewInstance.SetActive(false);
    }
}
