using System;
using System.Collections.Generic;
using UnityEngine;

/*
 * Define los tipos compartidos del sistema de paredes destructibles.
 * Incluye fuentes de forma, ejes, datos de stamps, informacion de regiones
 * desprendidas y utilidades para trabajar con orientaciones de pared.
 */
public enum WallShapeSource
{
    Manual,
    BoxCollider,
    MeshCollider,
    MeshFilter
}

public enum WallFrontAxis
{
    Z,
    X,
    Y
}

public enum StampShape
{
    Circle,
    Box,
    Capsule,
    Ellipse
}

[Flags]
public enum SupportEdgeMask
{
    None = 0,
    Bottom = 1 << 0,
    Top = 1 << 1,
    Left = 1 << 2,
    Right = 1 << 3,
    All = Bottom | Top | Left | Right
}

[Serializable]
public struct DamageStampData
{
    public StampShape shape;
    public Vector2 center;
    public float radius;
    public Vector2 size;
    public float capsuleRadius;
    public float capsuleLength;
    public float ellipseRadiusX;
    public float ellipseRadiusY;
    public float rotationDeg;

    /*
     * Crea datos normalizados para las formas de dano soportadas por la pared.
     * Estos metodos concentran la validacion minima de radios y tamanos para que
     * el resto del sistema pueda construir stamps sin repetir reglas.
     */
    public static DamageStampData Circle(Vector2 center, float radius)
    {
        return new DamageStampData
        {
            shape = StampShape.Circle,
            center = center,
            radius = Mathf.Max(0.001f, radius)
        };
    }

    public static DamageStampData Box(Vector2 center, Vector2 size, float rotationDeg = 0f)
    {
        size.x = Mathf.Max(0.001f, size.x);
        size.y = Mathf.Max(0.001f, size.y);
        return new DamageStampData
        {
            shape = StampShape.Box,
            center = center,
            size = size,
            rotationDeg = rotationDeg
        };
    }

    public static DamageStampData Capsule(Vector2 center, float radius, float length, float rotationDeg = 0f)
    {
        return new DamageStampData
        {
            shape = StampShape.Capsule,
            center = center,
            capsuleRadius = Mathf.Max(0.001f, radius),
            capsuleLength = Mathf.Max(0f, length),
            rotationDeg = rotationDeg
        };
    }

    public static DamageStampData Ellipse(Vector2 center, float radiusX, float radiusY, float rotationDeg = 0f)
    {
        return new DamageStampData
        {
            shape = StampShape.Ellipse,
            center = center,
            ellipseRadiusX = Mathf.Max(0.001f, radiusX),
            ellipseRadiusY = Mathf.Max(0.001f, radiusY),
            rotationDeg = rotationDeg
        };
    }
}

[Serializable]
public struct DetachedRegionInfo
{
    public int id;
    public int cellCount;
    public RectInt bounds;
    public Vector2 localCenter;
    public Vector3 localCenter3D;
    public WallFrontAxis frontAxis;
    public bool qualifiesAsLargeChunk;
}

[Serializable]
public struct DebrisRegionInfo
{
    public int sourceRegionId;
    public int cellCount;
    public Vector2 localCenter;
    public Vector3 localSize;
    public Vector3 localCenter3D;
    public Vector3 localSize3D;
    public WallFrontAxis frontAxis;
    public bool hasResolvedLocal3D;
    public bool spawnedAsPhysicsDebris;
    public bool usedExactRegionVisual;
}

public sealed class DebrisBuildData
{
    public int sourceRegionId;
    public int cellCount;
    public Vector2 localCenter;
    public Vector3 localSize;
    public RectInt cellBounds;
    public readonly List<Vector2Int> cells = new List<Vector2Int>(64);
}

public struct RectIntRun
{
    public int xMin;
    public int xMax;
    public int yMin;
    public int yMax;

    public RectIntRun(int xMin, int xMax, int yMin, int yMax)
    {
        this.xMin = xMin;
        this.xMax = xMax;
        this.yMin = yMin;
        this.yMax = yMax;
    }
}

public struct CellPos
{
    public int x;
    public int y;

    public CellPos(int x, int y)
    {
        this.x = x;
        this.y = y;
    }
}

public static class WallAxisUtility
{
    /*
     * Resuelve los ejes locales usados por una pared segun su eje frontal.
     * Devuelve los indices y direcciones de ancho, alto y profundidad para que
     * mesh, colision y conversiones espaciales usen la misma base.
     */
    public static void GetBasis(
        WallFrontAxis frontAxis,
        out int uAxis,
        out int vAxis,
        out int nAxis,
        out Vector3 uDir,
        out Vector3 vDir,
        out Vector3 nDir)
    {
        switch (frontAxis)
        {
            case WallFrontAxis.X:
                uAxis = 2;
                vAxis = 1;
                nAxis = 0;
                uDir = Vector3.back;
                vDir = Vector3.up;
                nDir = Vector3.right;
                break;

            case WallFrontAxis.Y:
                uAxis = 0;
                vAxis = 2;
                nAxis = 1;
                uDir = Vector3.right;
                vDir = Vector3.back;
                nDir = Vector3.up;
                break;

            default:
                uAxis = 0;
                vAxis = 1;
                nAxis = 2;
                uDir = Vector3.right;
                vDir = Vector3.up;
                nDir = Vector3.forward;
                break;
        }
    }

    public static float GetAxis(Vector3 vector, int axis)
    {
        switch (axis)
        {
            case 0: return vector.x;
            case 1: return vector.y;
            default: return vector.z;
        }
    }

    public static void SetAxis(ref Vector3 vector, int axis, float value)
    {
        switch (axis)
        {
            case 0:
                vector.x = value;
                break;
            case 1:
                vector.y = value;
                break;
            default:
                vector.z = value;
                break;
        }
    }

    /*
     * Compone un Vector3 colocando valores en ejes arbitrarios.
     * Es una ayuda para construir tamanos y offsets de pared sin asumir que el
     * frente siempre esta sobre el eje Z.
     */
    public static Vector3 ComposeVector(
        int uAxis,
        float uValue,
        int vAxis,
        float vValue,
        int nAxis,
        float nValue)
    {
        Vector3 result = Vector3.zero;
        SetAxis(ref result, uAxis, uValue);
        SetAxis(ref result, vAxis, vValue);
        SetAxis(ref result, nAxis, nValue);
        return result;
    }
}
