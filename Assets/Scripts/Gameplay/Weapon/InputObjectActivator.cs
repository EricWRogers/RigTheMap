using Unity.MP_FPS;
using Unity.NetCode;
using UnityEngine;

public class InputObjectActivator : MonoBehaviour
{
    private Transform[] weaponObjects;
    private PlayerGhost playerGhost;

    private void Awake()
    {
        weaponObjects = GetComponentsInChildren<Transform>(true);
        playerGhost = GetComponent<PlayerGhost>();
    }

    private void OnEnable()
    {
        ClientInputReaderSystem.SelectPrimaryRequested += SelectPrimary;
        ClientInputReaderSystem.SelectSecondaryRequested += SelectSecondary;
    }

    private void OnDisable()
    {
        ClientInputReaderSystem.SelectPrimaryRequested -= SelectPrimary;
        ClientInputReaderSystem.SelectSecondaryRequested -= SelectSecondary;
    }

    private void SelectPrimary()
    {
        if (IsLocallyOwned())
            SetWeaponVisuals(useSecondary: false);
    }

    private void SelectSecondary()
    {
        if (IsLocallyOwned())
            SetWeaponVisuals(useSecondary: true);
    }

    private bool IsLocallyOwned()
    {
        return playerGhost != null && playerGhost.Role == MultiplayerRole.ClientOwned;
    }

    private void SetWeaponVisuals(bool useSecondary)
    {
        foreach (var weapon in weaponObjects)
        {
            if (weapon.CompareTag("Primary"))
                weapon.gameObject.SetActive(!useSecondary);
            else if (weapon.CompareTag("Secondary"))
                weapon.gameObject.SetActive(useSecondary);
        }
    }
}
