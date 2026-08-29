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
                    return Weapons != null && Weapons.Count > 2 ? 2u : 1u;
                default:
                    return 0;
            }
        }
    }
}