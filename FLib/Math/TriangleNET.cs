﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace FLib
{
    // Triangle.NET(http://triangle.codeplex.com/)のラッパ
    public class TriangleNET
    {
        public static List<FTriangle> Triangulate(List<PointF> path, float minAngle = 20, float maxAngle = 180, bool conformingDelaunay = true, bool quality = true, bool convex = false)
        {
            var mesh = TriangulateMesh(path, minAngle, maxAngle, conformingDelaunay, quality, convex);
            if (mesh == null)
                return null;

            var outTris = new List<FTriangle>();
            foreach (var t in mesh.Triangles)
                outTris.Add(new FTriangle(VertexToPoint(t.GetVertex(0)), VertexToPoint(t.GetVertex(1)), VertexToPoint(t.GetVertex(2))));

            return outTris;
        }

        public static void Triangulate(List<PointF> path, List<PointF> outPts, List<TriMesh> outTris, float minAngle = 20, float maxAngle = 180, bool conformingDelaunay = true, bool quality = true, bool convex = false)
        {
            var mesh = TriangulateMesh(path, minAngle, maxAngle, conformingDelaunay, quality, convex);
            if (mesh == null)
                return;

            if (outPts == null || outTris == null)
                return;

            outPts.Clear();
            outTris.Clear();

            Dictionary<PointF, int> ptToIdx = new Dictionary<PointF, int>();

            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                PointF pt = VertexToPoint(mesh.Vertices.ElementAt(i));
                outPts.Add(pt);
                ptToIdx[pt] = i;
            }

            foreach (var t in mesh.Triangles)
            {
                outTris.Add(new TriMesh(
                    ptToIdx[VertexToPoint(t.GetVertex(0))],
                    ptToIdx[VertexToPoint(t.GetVertex(1))],
                    ptToIdx[VertexToPoint(t.GetVertex(2))]
                ));
            }
        }

        public static TriangleNet.Mesh TriangulateMesh(List<PointF> path, float minAngle, float maxAngle, bool conformingDelaunay , bool quality , bool convex )
        {
            if (path == null)
                return null;
            if (path.Count <= 2)
                return null;

            List<FTriangle> outTris = new List<FTriangle>();

            MeshRenderer.Core.RenderData renderData = new MeshRenderer.Core.RenderData();
            MeshRenderer.Core.RenderManager renderManager = new MeshRenderer.Core.RenderManager();
            TriangleNet.Geometry.InputGeometry input = new TriangleNet.Geometry.InputGeometry(path.Count);
            TriangleNet.Mesh mesh = new TriangleNet.Mesh();

            input.AddPoint(path[0].X, path[0].Y);
            for (int i = 1; i < path.Count; i++)
            {
                input.AddPoint(path[i].X, path[i].Y);
                input.AddSegment(i - 1, i);
            }
            input.AddSegment(path.Count - 1, 0);

            renderData.SetInputGeometry(input);

            renderManager.CreateDefaultControl();
            renderManager.SetData(renderData);

            mesh.Behavior.MinAngle = FMath.Clamp(0, 40, minAngle);
            mesh.Behavior.MaxAngle = FMath.Clamp(80, 180, maxAngle);
            mesh.Behavior.ConformingDelaunay = conformingDelaunay;
            mesh.Behavior.Quality = quality;
            mesh.Behavior.Convex = convex;

            mesh.Triangulate(input);

            return mesh;
        }

        static PointF VertexToPoint(TriangleNet.Data.Vertex v)
        {
            return new PointF((float)v.X, (float)v.Y);
        }
    }
    
        public struct TriMesh
        {
            public int idx0;
            public int idx1;
            public int idx2;
            public TriMesh(int idx0, int idx1, int idx2)
            {
                this.idx0 = idx0;
                this.idx1 = idx1;
                this.idx2 = idx2;
            }
        }

}
