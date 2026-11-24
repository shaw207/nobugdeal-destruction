using UnityEngine;

/*
 * Define las formas de dano 2D usadas sobre el plano local de una pared.
 * Cada stamp sabe comprobar si una celda cae dentro de su area y expone bounds
 * para limitar la busqueda en el grid.
 */
public interface IDamageStamp2D
{
    bool Contains(Vector2 localPoint);
    Rect GetBounds();
}

public static class DamageStampFactory
{
    /*
     * Convierte datos serializables de dano en una forma ejecutable.
     * Centraliza la seleccion de circle, box, capsule o ellipse para que el grid
     * pueda aplicar cualquier arma mediante la misma interfaz.
     */
    public static IDamageStamp2D Build(in DamageStampData data)
    {
        switch (data.shape)
        {
            case StampShape.Circle:
                return new CircleStamp(data.center, data.radius);
            case StampShape.Box:
                return new BoxStamp2D(data.center, data.size, data.rotationDeg);
            case StampShape.Capsule:
                return new CapsuleStamp2D(data.center, data.capsuleRadius, data.capsuleLength, data.rotationDeg);
            case StampShape.Ellipse:
                return new EllipseStamp2D(data.center, data.ellipseRadiusX, data.ellipseRadiusY, data.rotationDeg);
            default:
                return new CircleStamp(data.center, Mathf.Max(0.001f, data.radius));
        }
    }

    public static Vector2 Rotate(Vector2 v, float deg)
    {
        float rad = deg * Mathf.Deg2Rad;
        float sin = Mathf.Sin(rad);
        float cos = Mathf.Cos(rad);
        return new Vector2(cos * v.x - sin * v.y, sin * v.x + cos * v.y);
    }
}

public sealed class CircleStamp : IDamageStamp2D
{
    private readonly Vector2 _center;
    private readonly float _radius;
    private readonly float _radiusSqr;

    public CircleStamp(Vector2 center, float radius)
    {
        _center = center;
        _radius = Mathf.Max(0.001f, radius);
        _radiusSqr = _radius * _radius;
    }

    public bool Contains(Vector2 localPoint)
    {
        return (localPoint - _center).sqrMagnitude <= _radiusSqr;
    }

    public Rect GetBounds()
    {
        Vector2 ext = new Vector2(_radius, _radius);
        return new Rect(_center - ext, ext * 2f);
    }
}

public sealed class BoxStamp2D : IDamageStamp2D
{
    private readonly Vector2 _center;
    private readonly Vector2 _halfSize;
    private readonly Quaternion _inverseRotation;
    private readonly float _rotationDeg;

    public BoxStamp2D(Vector2 center, Vector2 size, float rotationDeg)
    {
        _center = center;
        _halfSize = new Vector2(Mathf.Max(0.001f, size.x * 0.5f), Mathf.Max(0.001f, size.y * 0.5f));
        _rotationDeg = rotationDeg;
        _inverseRotation = Quaternion.Euler(0f, 0f, -rotationDeg);
    }

    public bool Contains(Vector2 localPoint)
    {
        Vector2 p = _inverseRotation * (localPoint - _center);
        return Mathf.Abs(p.x) <= _halfSize.x && Mathf.Abs(p.y) <= _halfSize.y;
    }

    public Rect GetBounds()
    {
        Vector2[] corners =
        {
            DamageStampFactory.Rotate(new Vector2(-_halfSize.x, -_halfSize.y), _rotationDeg) + _center,
            DamageStampFactory.Rotate(new Vector2( _halfSize.x, -_halfSize.y), _rotationDeg) + _center,
            DamageStampFactory.Rotate(new Vector2( _halfSize.x,  _halfSize.y), _rotationDeg) + _center,
            DamageStampFactory.Rotate(new Vector2(-_halfSize.x,  _halfSize.y), _rotationDeg) + _center,
        };

        Vector2 min = corners[0];
        Vector2 max = corners[0];
        for (int i = 1; i < corners.Length; i++)
        {
            min = Vector2.Min(min, corners[i]);
            max = Vector2.Max(max, corners[i]);
        }

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
}

public sealed class CapsuleStamp2D : IDamageStamp2D
{
    private readonly Vector2 _center;
    private readonly float _radius;
    private readonly float _halfSegment;
    private readonly Quaternion _inverseRotation;
    private readonly float _rotationDeg;

    public CapsuleStamp2D(Vector2 center, float radius, float length, float rotationDeg)
    {
        _center = center;
        _radius = Mathf.Max(0.001f, radius);
        _halfSegment = Mathf.Max(0f, length * 0.5f);
        _rotationDeg = rotationDeg;
        _inverseRotation = Quaternion.Euler(0f, 0f, -rotationDeg);
    }

    public bool Contains(Vector2 localPoint)
    {
        Vector2 p = _inverseRotation * (localPoint - _center);
        float clampedY = Mathf.Clamp(p.y, -_halfSegment, _halfSegment);
        Vector2 closest = new Vector2(0f, clampedY);
        return (p - closest).sqrMagnitude <= _radius * _radius;
    }

    public Rect GetBounds()
    {
        Vector2 ext = new Vector2(_radius, _radius + _halfSegment);
        Vector2[] corners =
        {
            DamageStampFactory.Rotate(new Vector2(-ext.x, -ext.y), _rotationDeg) + _center,
            DamageStampFactory.Rotate(new Vector2( ext.x, -ext.y), _rotationDeg) + _center,
            DamageStampFactory.Rotate(new Vector2( ext.x,  ext.y), _rotationDeg) + _center,
            DamageStampFactory.Rotate(new Vector2(-ext.x,  ext.y), _rotationDeg) + _center,
        };

        Vector2 min = corners[0];
        Vector2 max = corners[0];
        for (int i = 1; i < corners.Length; i++)
        {
            min = Vector2.Min(min, corners[i]);
            max = Vector2.Max(max, corners[i]);
        }

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
}

public sealed class EllipseStamp2D : IDamageStamp2D
{
    private readonly Vector2 _center;
    private readonly float _radiusX;
    private readonly float _radiusY;
    private readonly float _invRadiusXSqr;
    private readonly float _invRadiusYSqr;
    private readonly Quaternion _inverseRotation;
    private readonly float _rotationDeg;

    public EllipseStamp2D(Vector2 center, float radiusX, float radiusY, float rotationDeg)
    {
        _center = center;
        _radiusX = Mathf.Max(0.001f, radiusX);
        _radiusY = Mathf.Max(0.001f, radiusY);
        _invRadiusXSqr = 1f / (_radiusX * _radiusX);
        _invRadiusYSqr = 1f / (_radiusY * _radiusY);
        _rotationDeg = rotationDeg;
        _inverseRotation = Quaternion.Euler(0f, 0f, -rotationDeg);
    }

    public bool Contains(Vector2 localPoint)
    {
        Vector2 p = _inverseRotation * (localPoint - _center);
        float v = p.x * p.x * _invRadiusXSqr + p.y * p.y * _invRadiusYSqr;
        return v <= 1f;
    }

    public Rect GetBounds()
    {
        float rad = _rotationDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        float extX = Mathf.Sqrt(_radiusX * _radiusX * cos * cos + _radiusY * _radiusY * sin * sin);
        float extY = Mathf.Sqrt(_radiusX * _radiusX * sin * sin + _radiusY * _radiusY * cos * cos);
        Vector2 ext = new Vector2(extX, extY);
        return new Rect(_center - ext, ext * 2f);
    }
}
