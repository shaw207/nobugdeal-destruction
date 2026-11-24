using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Construye las mallas usadas por las paredes destructibles.
 * Convierte el grid de celdas solidas en geometria visible, genera debris y
 * mantiene una proyeccion UV coherente para que los materiales se vean estables
 * aunque la pared pierda partes.
 */
public static class WallMeshBuilder
{
    private const float DefaultUvScale = 1f;

    /*
     * Genera la malla principal de la pared desde el grid de material.
     * Crea caras frontales, traseras y laterales solo para las celdas solidas,
     * dejando huecos donde el dano ya elimino material.
     */
    public static Mesh BuildWallMesh(
        WallMaterialGrid grid,
        Vector3 localCenter,
        Vector3 localSize,
        WallFrontAxis frontAxis,
        Vector2? wallPlaneOffsetOverride = null)
    {
        Mesh mesh = new Mesh
        {
            name = "DestructibleWallMesh",
            indexFormat = IndexFormat.UInt32
        };

        List<Vector3> vertices = new List<Vector3>(2048);
        List<int> triangles = new List<int>(4096);
        List<Vector3> normals = new List<Vector3>(2048);
        List<Vector2> uvs = new List<Vector2>(2048);

        WallAxisUtility.GetBasis(
            frontAxis,
            out int uAxis,
            out int vAxis,
            out int nAxis,
            out Vector3 uDir,
            out Vector3 vDir,
            out Vector3 nDir);

        bool[,] solid = grid.Solid;
        float halfW = WallAxisUtility.GetAxis(localSize, uAxis) * 0.5f;
        float halfH = WallAxisUtility.GetAxis(localSize, vAxis) * 0.5f;
        float halfT = WallAxisUtility.GetAxis(localSize, nAxis) * 0.5f;
        float stepX = grid.CellWidth;
        float stepY = grid.CellHeight;
        Vector2 wallPlaneOffset = wallPlaneOffsetOverride ?? ResolveWallPlaneCenterOffset(localCenter, frontAxis);
        float wallPlaneUvWidth = Mathf.Abs(WallAxisUtility.GetAxis(localSize, uAxis)) * DefaultUvScale;

        Vector3 P(float u, float v, float n)
        {
            return localCenter + uDir * u + vDir * v + nDir * n;
        }

        for (int y = 0; y < grid.Height; y++)
        {
            float runStart = -halfW;
            for (int x = 0; x < grid.Width; x++)
            {
                if (!solid[x, y])
                    continue;

                float x0 = -halfW + x * stepX;
                float x1 = x0 + stepX;
                float y0 = -halfH + y * stepY;
                float y1 = y0 + stepY;

                bool hasLeft = x > 0 && solid[x - 1, y];
                bool hasRight = x < grid.Width - 1 && solid[x + 1, y];
                bool hasDown = y > 0 && solid[x, y - 1];
                bool hasUp = y < grid.Height - 1 && solid[x, y + 1];

                if (!hasLeft)
                    runStart = x0;

                // Las caras planas comparten una proyección UV continua a lo largo de cada tramo.
                if (!hasRight)
                {
                    Vector3 f0 = P(runStart, y0, halfT);
                    Vector3 f1 = P(x1, y0, halfT);
                    Vector3 f2 = P(x1, y1, halfT);
                    Vector3 f3 = P(runStart, y1, halfT);

                    Vector3 b0 = P(x1, y0, -halfT);
                    Vector3 b1 = P(runStart, y0, -halfT);
                    Vector3 b2 = P(runStart, y1, -halfT);
                    Vector3 b3 = P(x1, y1, -halfT);

                    AddWallPlaneQuadProjected(vertices, triangles, normals, uvs, f0, f1, f2, f3, nDir, frontAxis, DefaultUvScale, wallPlaneOffset, wallPlaneUvWidth, mirrorU: true);
                    AddWallPlaneQuadProjected(vertices, triangles, normals, uvs, b0, b1, b2, b3, -nDir, frontAxis, DefaultUvScale, wallPlaneOffset, wallPlaneUvWidth, mirrorU: false);
                }

                if (!hasLeft)
                {
                    AddQuadProjected(
                        vertices, triangles, normals, uvs,
                        P(x0, y0, -halfT), P(x0, y0, halfT), P(x0, y1, halfT), P(x0, y1, -halfT),
                        -uDir, UvProjectionMode.SideU, frontAxis, DefaultUvScale, wallPlaneOffset);
                }

                if (!hasRight)
                {
                    AddQuadProjected(
                        vertices, triangles, normals, uvs,
                        P(x1, y0, halfT), P(x1, y0, -halfT), P(x1, y1, -halfT), P(x1, y1, halfT),
                        uDir, UvProjectionMode.SideU, frontAxis, DefaultUvScale, wallPlaneOffset);
                }

                if (!hasDown)
                {
                    AddQuadProjected(
                        vertices, triangles, normals, uvs,
                        P(x0, y0, -halfT), P(x1, y0, -halfT), P(x1, y0, halfT), P(x0, y0, halfT),
                        -vDir, UvProjectionMode.SideV, frontAxis, DefaultUvScale, wallPlaneOffset);
                }

                if (!hasUp)
                {
                    AddQuadProjected(
                        vertices, triangles, normals, uvs,
                        P(x0, y1, halfT), P(x1, y1, halfT), P(x1, y1, -halfT), P(x0, y1, -halfT),
                        vDir, UvProjectionMode.SideV, frontAxis, DefaultUvScale, wallPlaneOffset);
                }
            }
        }

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Vector2 ResolveWallPlaneCenterOffsetForWall(
        Vector3 localWallCenter,
        WallFrontAxis frontAxis)
    {
        return ResolveWallPlaneCenterOffset(localWallCenter, frontAxis);
    }

    public static Vector2 ResolveWallPlaneTopLeftOffsetForWall(
        Vector3 localWallCenter,
        Vector3 localWallSize,
        WallFrontAxis frontAxis)
    {
        WallAxisUtility.GetBasis(
            frontAxis,
            out int uAxis,
            out int vAxis,
            out _,
            out Vector3 uDir,
            out Vector3 vDir,
            out _);

        float halfW = Mathf.Abs(WallAxisUtility.GetAxis(localWallSize, uAxis)) * 0.5f;
        float halfH = Mathf.Abs(WallAxisUtility.GetAxis(localWallSize, vAxis)) * 0.5f;

        Vector3 topLeftLocalPos = localWallCenter - uDir * halfW + vDir * halfH;
        Vector2 projectedTopLeft = ProjectWallPlane(topLeftLocalPos, frontAxis, 1f, Vector2.zero);
        return -projectedTopLeft;
    }

    public static Mesh BuildBoundsDebrisMesh(Vector3 localSize, WallFrontAxis frontAxis)
    {
        return BuildBoundsDebrisMesh(localSize, frontAxis, Vector3.zero, null, Vector3.zero, Quaternion.identity);
    }

    /*
     * Crea una malla simple de debris usando el volumen rectangular de la region.
     * Sirve como fallback barato cuando no hace falta representar cada celda
     * exacta del fragmento desprendido.
     */
    public static Mesh BuildBoundsDebrisMesh(
        Vector3 localSize,
        WallFrontAxis frontAxis,
        Vector3 localWallCenter,
        Transform wallTransform,
        Vector3 worldPos,
        Quaternion worldRot)
    {
        Mesh mesh = new Mesh
        {
            name = "WallDebrisBoundsMesh",
            indexFormat = IndexFormat.UInt32
        };

        WallAxisUtility.GetBasis(
            frontAxis,
            out _,
            out _,
            out _,
            out Vector3 uDir,
            out Vector3 vDir,
            out Vector3 nDir);

        float hx = localSize.x * 0.5f;
        float hy = localSize.y * 0.5f;
        float hz = localSize.z * 0.5f;

        List<Vector3> vertices = new List<Vector3>(24);
        List<int> triangles = new List<int>(36);
        List<Vector3> normals = new List<Vector3>(24);
        List<Vector2> uvs = new List<Vector2>(24);

        Vector3 P(float u, float v, float n)
        {
            return uDir * u + vDir * v + nDir * n;
        }

        Vector2 wallPlaneOffset = ResolveWallPlaneOffset(wallTransform, worldPos, localWallCenter, frontAxis);

        Vector3 f0 = P(-hx, -hy, hz);
        Vector3 f1 = P(hx, -hy, hz);
        Vector3 f2 = P(hx, hy, hz);
        Vector3 f3 = P(-hx, hy, hz);

        Vector3 b0 = P(hx, -hy, -hz);
        Vector3 b1 = P(-hx, -hy, -hz);
        Vector3 b2 = P(-hx, hy, -hz);
        Vector3 b3 = P(hx, hy, -hz);

        AddQuadProjected(vertices, triangles, normals, uvs, f0, f1, f2, f3, nDir, UvProjectionMode.WallPlane, frontAxis, DefaultUvScale, wallPlaneOffset);
        AddQuadProjected(vertices, triangles, normals, uvs, b0, b1, b2, b3, -nDir, UvProjectionMode.WallPlane, frontAxis, DefaultUvScale, wallPlaneOffset);

        AddQuadProjected(vertices, triangles, normals, uvs,
            P(-hx, -hy, -hz), P(-hx, -hy, hz), P(-hx, hy, hz), P(-hx, hy, -hz),
            -uDir, UvProjectionMode.SideU, frontAxis, DefaultUvScale, wallPlaneOffset);

        AddQuadProjected(vertices, triangles, normals, uvs,
            P(hx, -hy, hz), P(hx, -hy, -hz), P(hx, hy, -hz), P(hx, hy, hz),
            uDir, UvProjectionMode.SideU, frontAxis, DefaultUvScale, wallPlaneOffset);

        AddQuadProjected(vertices, triangles, normals, uvs,
            P(-hx, hy, hz), P(hx, hy, hz), P(hx, hy, -hz), P(-hx, hy, -hz),
            vDir, UvProjectionMode.SideV, frontAxis, DefaultUvScale, wallPlaneOffset);

        AddQuadProjected(vertices, triangles, normals, uvs,
            P(-hx, -hy, -hz), P(hx, -hy, -hz), P(hx, -hy, hz), P(-hx, -hy, hz),
            -vDir, UvProjectionMode.SideV, frontAxis, DefaultUvScale, wallPlaneOffset);

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Mesh BuildExactDebrisMesh(
        DebrisBuildData data,
        WallMaterialGrid grid,
        WallFrontAxis frontAxis,
        float thickness)
    {
        return BuildExactDebrisMesh(data, grid, frontAxis, thickness, Vector3.zero, null, Vector3.zero, Quaternion.identity);
    }

    /*
     * Crea una malla de debris que conserva la forma exacta de las celdas sueltas.
     * Se usa para que los fragmentos grandes mantengan una silueta parecida al
     * trozo real separado de la pared.
     */
    public static Mesh BuildExactDebrisMesh(
        DebrisBuildData data,
        WallMaterialGrid grid,
        WallFrontAxis frontAxis,
        float thickness,
        Vector3 localWallCenter,
        Transform wallTransform,
        Vector3 worldPos,
        Quaternion worldRot)
    {
        Mesh mesh = new Mesh
        {
            name = "WallDebrisExactRegionMesh",
            indexFormat = IndexFormat.UInt32
        };

        List<Vector3> vertices = new List<Vector3>(data.cellCount * 24);
        List<int> triangles = new List<int>(data.cellCount * 36);
        List<Vector3> normals = new List<Vector3>(data.cellCount * 24);
        List<Vector2> uvs = new List<Vector2>(data.cellCount * 24);

        HashSet<int> occupied = new HashSet<int>(data.cellCount * 2);
        int keyWidth = grid.Width;
        for (int i = 0; i < data.cells.Count; i++)
            occupied.Add(data.cells[i].y * keyWidth + data.cells[i].x);

        WallAxisUtility.GetBasis(
            frontAxis,
            out _,
            out _,
            out _,
            out Vector3 uDir,
            out Vector3 vDir,
            out Vector3 nDir);

        float stepX = grid.CellWidth;
        float stepY = grid.CellHeight;
        float halfCellW = stepX * 0.5f;
        float halfCellH = stepY * 0.5f;
        float halfT = thickness * 0.5f;

        Vector3 P(float u, float v, float n)
        {
            return uDir * u + vDir * v + nDir * n;
        }

        Vector2 wallPlaneOffset = data.localCenter;

        for (int i = 0; i < data.cells.Count; i++)
        {
            Vector2Int c = data.cells[i];
            Vector2 cellCenterAbs = grid.CellCenterLocal(c.x, c.y);
            Vector2 cellCenter = cellCenterAbs - data.localCenter;

            float x0 = cellCenter.x - halfCellW;
            float x1 = cellCenter.x + halfCellW;
            float y0 = cellCenter.y - halfCellH;
            float y1 = cellCenter.y + halfCellH;

            Vector3 f0 = P(x0, y0, halfT);
            Vector3 f1 = P(x1, y0, halfT);
            Vector3 f2 = P(x1, y1, halfT);
            Vector3 f3 = P(x0, y1, halfT);

            Vector3 b0 = P(x1, y0, -halfT);
            Vector3 b1 = P(x0, y0, -halfT);
            Vector3 b2 = P(x0, y1, -halfT);
            Vector3 b3 = P(x1, y1, -halfT);

            AddQuadProjected(vertices, triangles, normals, uvs, f0, f1, f2, f3, nDir, UvProjectionMode.WallPlane, frontAxis, DefaultUvScale, wallPlaneOffset);
            AddQuadProjected(vertices, triangles, normals, uvs, b0, b1, b2, b3, -nDir, UvProjectionMode.WallPlane, frontAxis, DefaultUvScale, wallPlaneOffset);

            bool hasLeft = occupied.Contains(c.y * keyWidth + (c.x - 1));
            bool hasRight = occupied.Contains(c.y * keyWidth + (c.x + 1));
            bool hasDown = occupied.Contains((c.y - 1) * keyWidth + c.x);
            bool hasUp = occupied.Contains((c.y + 1) * keyWidth + c.x);

            if (!hasLeft)
            {
                AddQuadProjected(vertices, triangles, normals, uvs,
                    P(x0, y0, -halfT), P(x0, y0, halfT), P(x0, y1, halfT), P(x0, y1, -halfT),
                    -uDir, UvProjectionMode.SideU, frontAxis, DefaultUvScale, wallPlaneOffset);
            }

            if (!hasRight)
            {
                AddQuadProjected(vertices, triangles, normals, uvs,
                    P(x1, y0, halfT), P(x1, y0, -halfT), P(x1, y1, -halfT), P(x1, y1, halfT),
                    uDir, UvProjectionMode.SideU, frontAxis, DefaultUvScale, wallPlaneOffset);
            }

            if (!hasDown)
            {
                AddQuadProjected(vertices, triangles, normals, uvs,
                    P(x0, y0, -halfT), P(x1, y0, -halfT), P(x1, y0, halfT), P(x0, y0, halfT),
                    -vDir, UvProjectionMode.SideV, frontAxis, DefaultUvScale, wallPlaneOffset);
            }

            if (!hasUp)
            {
                AddQuadProjected(vertices, triangles, normals, uvs,
                    P(x0, y1, halfT), P(x1, y1, halfT), P(x1, y1, -halfT), P(x0, y1, -halfT),
                    vDir, UvProjectionMode.SideV, frontAxis, DefaultUvScale, wallPlaneOffset);
            }
        }

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateBounds();
        return mesh;
    }

    private enum UvProjectionMode
    {
        WallPlane,
        SideU,
        SideV
    }

    private static void AddQuadProjected(
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector3> normals,
        List<Vector2> uvs,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 normal,
        UvProjectionMode mode,
        WallFrontAxis frontAxis,
        float uvScale,
        Vector2 wallPlaneOffset)
    {
        Vector2 uvA = ProjectUV(a, mode, frontAxis, uvScale, wallPlaneOffset);
        Vector2 uvB = ProjectUV(b, mode, frontAxis, uvScale, wallPlaneOffset);
        Vector2 uvC = ProjectUV(c, mode, frontAxis, uvScale, wallPlaneOffset);
        Vector2 uvD = ProjectUV(d, mode, frontAxis, uvScale, wallPlaneOffset);

        AddQuad(vertices, triangles, normals, uvs, a, b, c, d, normal, uvA, uvB, uvC, uvD);
    }

    private static void AddWallPlaneQuadProjected(
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector3> normals,
        List<Vector2> uvs,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 normal,
        WallFrontAxis frontAxis,
        float uvScale,
        Vector2 wallPlaneOffset,
        float wallPlaneUvWidth,
        bool mirrorU)
    {
        Vector2 uvA = ProjectWallPlane(a, frontAxis, uvScale, wallPlaneOffset);
        Vector2 uvB = ProjectWallPlane(b, frontAxis, uvScale, wallPlaneOffset);
        Vector2 uvC = ProjectWallPlane(c, frontAxis, uvScale, wallPlaneOffset);
        Vector2 uvD = ProjectWallPlane(d, frontAxis, uvScale, wallPlaneOffset);

        if (mirrorU)
        {
            uvA.x = wallPlaneUvWidth - uvA.x;
            uvB.x = wallPlaneUvWidth - uvB.x;
            uvC.x = wallPlaneUvWidth - uvC.x;
            uvD.x = wallPlaneUvWidth - uvD.x;
        }

        AddQuad(vertices, triangles, normals, uvs, a, b, c, d, normal, uvA, uvB, uvC, uvD);
    }

    private static Vector2 ProjectUV(
        Vector3 localPos,
        UvProjectionMode mode,
        WallFrontAxis frontAxis,
        float uvScale,
        Vector2 wallPlaneOffset)
    {
        switch (mode)
        {
            case UvProjectionMode.WallPlane:
                return ProjectWallPlane(localPos, frontAxis, uvScale, wallPlaneOffset);
            case UvProjectionMode.SideU:
                return ProjectSideU(localPos, frontAxis, uvScale, wallPlaneOffset);
            case UvProjectionMode.SideV:
                return ProjectSideV(localPos, frontAxis, uvScale, wallPlaneOffset);
            default:
                return Vector2.zero;
        }
    }

    private static Vector2 ProjectWallPlane(
        Vector3 localPos,
        WallFrontAxis frontAxis,
        float uvScale,
        Vector2 wallPlaneOffset)
    {
        switch (frontAxis)
        {
            case WallFrontAxis.Z:
                return new Vector2((localPos.x + wallPlaneOffset.x) * uvScale, (localPos.y + wallPlaneOffset.y) * uvScale);
            case WallFrontAxis.X:
                return new Vector2((-localPos.z + wallPlaneOffset.x) * uvScale, (localPos.y + wallPlaneOffset.y) * uvScale);
            case WallFrontAxis.Y:
                return new Vector2((localPos.x + wallPlaneOffset.x) * uvScale, (-localPos.z + wallPlaneOffset.y) * uvScale);
            default:
                return Vector2.zero;
        }
    }

    private static Vector2 ProjectSideU(
        Vector3 localPos,
        WallFrontAxis frontAxis,
        float uvScale,
        Vector2 wallPlaneOffset)
    {
        switch (frontAxis)
        {
            case WallFrontAxis.Z:
                return new Vector2(localPos.z * uvScale, (localPos.y + wallPlaneOffset.y) * uvScale);
            case WallFrontAxis.X:
                return new Vector2(localPos.x * uvScale, (localPos.y + wallPlaneOffset.y) * uvScale);
            case WallFrontAxis.Y:
                return new Vector2(localPos.y * uvScale, (-localPos.z + wallPlaneOffset.y) * uvScale);
            default:
                return Vector2.zero;
        }
    }

    private static Vector2 ProjectSideV(
        Vector3 localPos,
        WallFrontAxis frontAxis,
        float uvScale,
        Vector2 wallPlaneOffset)
    {
        switch (frontAxis)
        {
            case WallFrontAxis.Z:
                return new Vector2((localPos.x + wallPlaneOffset.x) * uvScale, localPos.z * uvScale);
            case WallFrontAxis.X:
                return new Vector2((-localPos.z + wallPlaneOffset.x) * uvScale, localPos.x * uvScale);
            case WallFrontAxis.Y:
                return new Vector2((localPos.x + wallPlaneOffset.x) * uvScale, localPos.y * uvScale);
            default:
                return Vector2.zero;
        }
    }

    private static Vector2 ResolveWallPlaneOffset(Transform wallTransform, Vector3 worldPos, Vector3 localWallCenter, WallFrontAxis frontAxis)
    {
        if (wallTransform == null)
            return Vector2.zero;

        Vector3 localPos = wallTransform.InverseTransformPoint(worldPos) - localWallCenter;

        switch (frontAxis)
        {
            case WallFrontAxis.Z:
                return new Vector2(localPos.x, localPos.y);
            case WallFrontAxis.X:
                return new Vector2(-localPos.z, localPos.y);
            case WallFrontAxis.Y:
                return new Vector2(localPos.x, -localPos.z);
            default:
                return Vector2.zero;
        }
    }

    private static Vector2 ResolveWallPlaneCenterOffset(Vector3 localWallCenter, WallFrontAxis frontAxis)
    {
        switch (frontAxis)
        {
            case WallFrontAxis.Z:
                return new Vector2(-localWallCenter.x, -localWallCenter.y);
            case WallFrontAxis.X:
                return new Vector2(localWallCenter.z, -localWallCenter.y);
            case WallFrontAxis.Y:
                return new Vector2(-localWallCenter.x, localWallCenter.z);
            default:
                return Vector2.zero;
        }
    }

    public static void AddQuad(
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector3> normals,
        List<Vector2> uvs,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 normal)
    {
        AddQuad(
            vertices,
            triangles,
            normals,
            uvs,
            a, b, c, d,
            normal,
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f));
    }

    public static void AddQuad(
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector3> normals,
        List<Vector2> uvs,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 normal,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC,
        Vector2 uvD)
    {
        int start = vertices.Count;

        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);

        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);

        uvs.Add(uvA);
        uvs.Add(uvB);
        uvs.Add(uvC);
        uvs.Add(uvD);

        triangles.Add(start + 0);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start + 0);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }
}
