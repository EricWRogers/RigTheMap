using UnityEngine;
using System.Collections.Generic;

namespace Unity.MP_FPS
{
    public enum WeaponType
    {
        Hitscan,
        Projectile
    }

    public enum ReticleType
    {
        Cross,
        TCross,
        OpenCircular,
        CircularCross
    }

    [CreateAssetMenu(fileName = "NewWeaponData", menuName = "FPS Sample/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        [Header("General")] public string WeaponName = "Assault Rifle";
        public WeaponType Type = WeaponType.Hitscan;
        public ReticleType ReticleType = ReticleType.TCross;

        [Header("Firing Mechanics")] [Tooltip("Shots per second")]
        public float CooldownInMs = 10f;

        public float Damage = 15f;

        [Header("Hitscan Properties")] [Tooltip("Max range for raycast-based weapons.")]
        public float HitscanRange = 100f;

        [Header("Placement Properties")] [Tooltip("If true, the weapon will place a ghost prefab instead of dealing damage.")]
        public bool IsPlacementWeapon = false;
        [Tooltip("Layers that a placement weapon is allowed to place on. Leave at default to use Ground/Default.")]
        public LayerMask PlacementLayerMask = default;
        [Tooltip("Distance to offset the placed prefab from the surface normal.")]
        public float PlacementOffset = 0.25f;

        [Header("Ammo & Reloading")] public int MagazineSize = 30;
        public float ReloadTime = 2.0f; // Time in seconds

        [Header("Projectile Properties")] [Tooltip("The ghost prefab for the projectile to be spawned.")]
        public GhostSpawner.GhostReference ProjectileGhostPrefab; // a reference to the ghost prefab for the projectile to be spawned

        public List<GhostSpawner.GhostReference> PlacementGhostPrefabs = new List<GhostSpawner.GhostReference>(); // List of ghost prefabs to spawn

        public GhostSpawner.GhostReference ProjectileHitVfxPrefab;
        public GhostSpawner.GhostReference MuzzleFlashVfxPrefab; 
        public SoundDef WeaponFireSfx;
        public SoundDef WeaponReloadSfx;

        public ProjectileBehavior Behavior = ProjectileBehavior.DirectDamage;
        public float AoeRadius = 5f;
        public float ProjectileSpeed = 30f;
    }

    public enum ProjectileBehavior
    {
        DirectDamage,
        AreaOfEffect
    }
}