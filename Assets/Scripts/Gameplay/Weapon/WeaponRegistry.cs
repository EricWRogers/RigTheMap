using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS
{
    [CreateAssetMenu(fileName = "WeaponRegistry", menuName = "FPS Sample/Weapon Registry")]
    public class WeaponRegistry : ScriptableObject
    {
        public List<WeaponData> Weapons;

        public WeaponData GetWeaponData(uint weaponID)
        {
            if (Weapons == null)
            {
                return null;
            }

            if (weaponID < Weapons.Count)
            {
                return Weapons[(int)weaponID];
            }

            return null; // or a default weapon
        }

        public uint GetWeaponIdForCharacter(int characterIndex)
        {
            switch (characterIndex)
            {
                case 0:
                    return 0;
                case 1:
                    return 1;
                case 2:
                    return 2;
                case 3:
                    return 3;
                case 4:
                    return 4;
                default:
                    return 0;
            }
        }

        public List<WeaponData> GetAvailablePlacementItems()
        {
            if (Weapons == null)
            {
                return new List<WeaponData>();
            }
            return Weapons.FindAll(w => w != null && w.IsPlacementWeapon);
        }
    }
}