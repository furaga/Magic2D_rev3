using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Windows;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using FLib.SharpDX;
using DXColor = SharpDX.Color;

namespace PatchworkLib.PatchMesh
{
    public class PatchMeshRenderer : IDisposable
    {
        // DrawPoint()で描画する点の深度。MeshやLineより手前がいいからなるべく小さい値にするべき。
        // TODO: 明示的に描画順を指定
        const float PointRenderDepth = -1f;
        const float LineRenderDepth = -0.5f;

        const float nearClip = 0.1f;
        const float farClip = 10000.0f;

        public bool rotateCamera = false;

        SharpDXInfo RenderInfo;
        Dictionary<string, Texture2D> textureDict = new Dictionary<string, Texture2D>();
        Texture2D whitePixel;


        float time = 0;



        /// <param name="setDefaultRenderInfo">PatchMeshRenderer.RenderInfoをSharpDXHelperがデフォルトで使用するrendering infoとして設定するか</param>
        public PatchMeshRenderer(IntPtr handle, Size size, bool setDefaultRenderInfo)
        {
            RenderInfo = SharpDXHelper.Initialize(handle, size, rawVertices, rawIndices, new Matrix(), "whitePixel.png");
            whitePixel = SharpDXHelper.LoadTexture(RenderInfo, "whitePixel.png");
            if (setDefaultRenderInfo)
                SharpDXHelper.SetDefaultRenderInfo(RenderInfo);
        }

        public void Dispose()
        {
            if (whitePixel != null)
                whitePixel.Dispose();
            if (RenderInfo != null)
                RenderInfo.Dispose();
        }


        public void BeginDraw()
        {
            if (rotateCamera)
                time += 0.02f;
            SharpDXHelper.BeginDraw(RenderInfo);
        }

        public void ClearDepthStencilView ()
        {
            SharpDXHelper.ClearDepthStencilView(RenderInfo);
        }

        public void EndDraw()
        {
            SharpDXHelper.EndDraw(RenderInfo);
        }

        /// <summary>
        /// メッシュを描画。
        /// cameraPositionは x, y : [-∞, +∞], z : [-∞, 0)
        /// x, y が大きいほどカメラが右下に移動する
        /// zが小さいほどズームアウトする
        /// </summary>
        /// <param name="cameraPosition">x, y : [-∞, +∞], z : [-∞, 0).x, y が大きいほどカメラが右下に移動する.zが小さいほどズームアウトする</param>
        public void DrawMesh(PatchMesh mesh, string textureKey, PatchMeshRenderResources resources, Size formSize, Vector3 cameraPosition)
        {
            DrawMesh(mesh, textureKey, resources, DXColor.White, formSize, cameraPosition);
        }

        /// <summary>
        /// メッシュを描画。
        /// cameraPositionは x, y : [-∞, +∞], z : [-∞, 0)
        /// x, y が大きいほどカメラが右下に移動する
        /// zが小さいほどズームアウトする
        /// </summary>
        /// <param name="cameraPosition">x, y : [-∞, +∞], z : [-∞, 0).x, y が大きいほどカメラが右下に移動する.zが小さいほどズームアウトする</param>
        public void DrawMesh(PatchMesh mesh, string textureKey, PatchMeshRenderResources resources, DXColor col, Size formSize, Vector3 cameraPosition)
        {
            List<VertexPositionColorTexture> rawVertices = new List<VertexPositionColorTexture>();
            for (int i = 0; i < mesh.vertices.Count; i++)
            {
                Vector3 pos = vec3(mesh.vertices[i].position);
                pos.Y *= -1;
                Vector2 coord = vec2(mesh.vertices[i].GetTexcoord(textureKey));
                rawVertices.Add(new VertexPositionColorTexture(pos, col, coord));
            }

            List<int> rawIndices = new List<int>();
            for (int i = 0; i < mesh.triangles.Count; i++)
            {
                if (mesh.triangles[i].TextureKey != textureKey)
                    continue;
                rawIndices.Add(mesh.triangles[i].Idx0);
                rawIndices.Add(mesh.triangles[i].Idx1);
                rawIndices.Add(mesh.triangles[i].Idx2);
            }

            var texture = resources.GetTexture(PatchMeshRenderResources.GenerateResourceKey(mesh, textureKey));

            Draw(rawVertices, rawIndices, texture, formSize, cameraPosition, PrimitiveTopology.TriangleList);
        }

        /// <summary>
        /// メッシュのワイヤフレームを描画
        /// </summary>
        public void DrawWireframe(PatchMesh mesh, string textureKey, DXColor col, Size formSize, Vector3 cameraPosition)
        {
            List<VertexPositionColorTexture> rawVertices = new List<VertexPositionColorTexture>();
            List<int> rawIndices = new List<int>();

            for (int i = 0; i < mesh.triangles.Count; i++)
            {
                if (mesh.triangles[i].TextureKey != textureKey)
                    continue;

                PointF p0 = mesh.vertices[mesh.triangles[i].Idx0].position;
                PointF p1 = mesh.vertices[mesh.triangles[i].Idx1].position;
                PointF p2 = mesh.vertices[mesh.triangles[i].Idx2].position;

                var pts = GetLineMeshPoint(p0, p1, 1);
                pts.AddRange(GetLineMeshPoint(p1, p2, 1));
                pts.AddRange(GetLineMeshPoint(p2, p0, 1));

                int offset = rawVertices.Count;

                foreach (var p in pts)
                {
                    Vector3 pos = vec3(p);
                    pos.Y *= -1;
                    pos.Z = LineRenderDepth;
                    rawVertices.Add(new VertexPositionColorTexture(pos, col, Vector2.Zero));
                }

                rawIndices.AddRange(
                    new []
                    {
                        0, 1, 2, 2, 1, 3,
                        4, 5, 6, 6, 5, 7,
                        8, 9, 10, 10, 9, 11,
                    }.Select(val => val + offset).ToArray());
            }

            Draw(rawVertices, rawIndices, whitePixel, formSize, cameraPosition, PrimitiveTopology.TriangleList);
        }

        /// <summary>
        /// 点を描画
        /// </summary>
        public void DrawPoint(PointF position, float pointSize, DXColor col, Size formSize, Vector3 cameraPosition)
        {
            List<VertexPositionColorTexture> rawVertices = new List<VertexPositionColorTexture>();
            for (int i = 0; i < 4; i++)
            {
                Vector3 pos = vec3(new PointF(position.X - pointSize * 0.5f + pointSize * (i % 2), position.Y - pointSize * 0.5f + pointSize * (i / 2)));
                pos.Y *= -1;
                pos.Z = PointRenderDepth;
                rawVertices.Add(new VertexPositionColorTexture(pos, col, Vector2.Zero));
            }

            List<int> rawIndices = new List<int>() { 0, 1, 2, 2, 1, 3 };

            Draw(rawVertices, rawIndices, whitePixel, formSize, cameraPosition, PrimitiveTopology.TriangleList);
        }

        /// <summary>
        /// 線を描画
        /// </summary>
        public void DrawLine(PointF start, PointF end, float lineWidth, DXColor col, Size formSize, Vector3 cameraPosition)
        {
            List<VertexPositionColorTexture> rawVertices = new List<VertexPositionColorTexture>();

            var pts = GetLineMeshPoint(start, end, lineWidth);
            foreach (var p in pts)
            {
                Vector3 pos = vec3(p);
                pos.Y *= -1;
                pos.Z = LineRenderDepth;
                rawVertices.Add(new VertexPositionColorTexture(pos, col, Vector2.Zero));
            }

            List<int> rawIndices = new List<int>() { 0, 1, 2, 2, 1, 3 };

            Draw(rawVertices, rawIndices, whitePixel, formSize, cameraPosition, PrimitiveTopology.TriangleList);
        }

        List<PointF> GetLineMeshPoint(PointF start, PointF end, float lineWidth)
        {
            float dx = end.X - start.X;
            float dy = end.Y - start.Y;
            float dist = FLib.FMath.Distance(start, end);
            if (dist <= 1e-4)
                return new List<PointF>();

            float nx = dy / dist * lineWidth * 0.5f;
            float ny = -dx / dist * lineWidth * 0.5f;

            var ls = new List<PointF>()
            {
                new PointF(start.X + nx, start.Y + ny),
                new PointF(start.X - nx, start.Y - ny),
                new PointF(end.X + nx, end.Y + ny),
                new PointF(end.X - nx, end.Y - ny),
            };

            return ls;
        }

        void Draw(List<VertexPositionColorTexture> rawVertices, List<int> rawIndices, Texture2D texture, Size formSize, Vector3 cameraPosition, PrimitiveTopology primitiveType)
        {
            var worldViewProj = CameraMatrix(formSize, cameraPosition);
            worldViewProj.Transpose();

            SharpDXHelper.UpdateVertexBuffer(RenderInfo, rawVertices);
            SharpDXHelper.UpdateIndexBuffer(RenderInfo, rawIndices);
            SharpDXHelper.UpdateCameraBuffer(RenderInfo, worldViewProj);
            if (texture != null)
                SharpDXHelper.SwitchTexture(RenderInfo, texture);

            SharpDXHelper.Draw(RenderInfo, primitiveType);
        }




        public Vector3 Unproject(System.Drawing.Point point, float z,Size formSize, Vector3 cameraPosition )
        {
            var worldViewProj = CameraMatrix(formSize, cameraPosition);

            Vector3 pointNear = new Vector3(point.X, point.Y, nearClip);
            Vector3 unprojectNear;
            Vector3.Unproject(ref pointNear, 0, 0, formSize.Width, formSize.Height, nearClip, farClip, ref worldViewProj, out unprojectNear);

            Vector3 pointFar = new Vector3(point.X, point.Y, farClip);
            Vector3 unprojectFar;
            Vector3.Unproject(ref pointFar, 0, 0, formSize.Width, formSize.Height, nearClip, farClip, ref worldViewProj, out unprojectFar);

            Vector3 pos = unprojectNear + (unprojectFar - unprojectNear) * (z - unprojectNear.Z) / (unprojectFar.Z - unprojectNear.Z);
            pos.Z = z;
            pos.Y = -pos.Y;

            return pos;
        }

        Matrix CameraMatrix(Size formSize, Vector3 cameraPosition)
        {
            var view = Matrix.LookAtLH(
                  new Vector3(cameraPosition.X, -cameraPosition.Y, cameraPosition.Z),
                  new Vector3(cameraPosition.X, -cameraPosition.Y, 0),
                  Vector3.UnitY);
            var proj = Matrix.PerspectiveFovLH((float)Math.PI / 4.0f, formSize.Width / (float)formSize.Height, nearClip,farClip);
            var viewProj = Matrix.Multiply(view, proj);
            // デバッグ用。ぐるぐるカメラを回転させたいとき
            // timeは BeginDraw() でインクリメントされる
            var worldViewProj = Matrix.RotationX(time) * Matrix.RotationY(time * 2) * Matrix.RotationZ(time * .7f) * viewProj;
            return worldViewProj;
        }



        Vector2 vec2(PointF pt)
        {
            return new Vector2(pt.X, pt.Y);
        }

        Vector3 vec3(PointF pt)
        {
            return new Vector3(pt.X, pt.Y, 0);
        }











        #region デバッグ用
        static VertexPositionColorTexture[] rawVertices = new[]
        {
            new VertexPositionColorTexture(new Vector3(-1,-1,-1), DXColor.Red, new Vector2(0, 0)),
            new VertexPositionColorTexture(new Vector3(1,-1,-1), DXColor.Blue, new Vector2(1, 0)),
            new VertexPositionColorTexture(new Vector3(-1,1,-1), DXColor.Yellow, new Vector2(1, 0)),
            new VertexPositionColorTexture(new Vector3(-1,-1,1), DXColor.Green, new Vector2(1, 1)),
            new VertexPositionColorTexture(new Vector3(1,1,-1), DXColor.Purple, new Vector2(1, 1)),
            new VertexPositionColorTexture(new Vector3(1,-1,1), DXColor.Cyan, new Vector2(0, 1)),
            new VertexPositionColorTexture(new Vector3(-1,1,1), DXColor.White, new Vector2(1, 0)),
            new VertexPositionColorTexture(new Vector3(1,1,1), DXColor.LightGreen, new Vector2(0, 0)),
        };

        static int[] rawIndices = new[]
        {
            0, 2, 4,
            0, 4, 1,

            3, 7, 6,
            3, 5, 7,
            
            2, 6, 7,
            2, 7, 4,

            0, 5, 3,
            0, 1, 5,

            0, 3, 6,
            0, 6, 2,

            1, 7, 5,
            1, 4, 7,
        };
        #endregion

        public void ToBitmap()
        {
//            var bmp = 
                SharpDXHelper.ToBitmap(RenderInfo);
        }
    }
}