using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace PatchworkLib.PatchMesh
{
    /// <summary>
    /// ! デバッグ用。PatchSkeletalMeshのメッシュ・切り口などをbmp画像に書き出して出力したい
    /// </summary>
    public class PatchSkeletalMeshRenderer
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="sections"></param>
        /// <param name="showPath"></param>
        /// <param name="alignment">描画画像がかならず画面の左上に来るように配置をずらすか</param>
        /// <returns></returns>
        public static Bitmap ToBitmap(PatchSkeletalMesh mesh, List<CharacterRange> sections = null, bool showPath = false, bool alignment = false, int w = 800, int h = 800)
        {
            int maxx = (int)mesh.mesh.vertices.Select(p => p.position.X).Max() + 1;
            int maxy = (int)mesh.mesh.vertices.Select(p => p.position.Y).Max() + 1;
            if (!alignment)
            {
                if (maxx <= 0 || maxy <= 0)
                    return null;
            }

            int minx = (int)mesh.mesh.vertices.Select(p => p.position.X).Min() - 1;
            int miny = (int)mesh.mesh.vertices.Select(p => p.position.Y).Min() - 1;
            PointF offset = alignment ? new PointF(-minx, -miny) : PointF.Empty;

            maxx = w;
            maxy = h;

            Bitmap bmp = new Bitmap(maxx, maxy, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            Brush[] pens = new Brush[] 
            {
                Brushes.Black,
                Brushes.Yellow,
                Brushes.Purple,
                Brushes.Pink,
                Brushes.DarkGreen,
                Brushes.DarkBlue,
            };
            
            Pen pen = new Pen(Brushes.Black);
            Pen penB = new Pen(Brushes.Blue);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);

                // メッシュ
                foreach (var t in mesh.mesh.triangles)
                {
                    PointF pt0 = mesh.mesh.vertices[t.Idx0].position;
                    PointF pt1 = mesh.mesh.vertices[t.Idx1].position;
                    PointF pt2 = mesh.mesh.vertices[t.Idx2].position;

                    g.DrawLines(pen, new PointF[] { 
                        Offset(pt0, offset),
                        Offset(pt1, offset),
                        Offset(pt2, offset),
                        Offset(pt0, offset)
                    });
                }
                
                // 頂点（partで色分け）
                foreach (var v in mesh.mesh.vertices)
                    g.FillRectangle(pens[v.part % pens.Length], v.position.X - 2 + offset.X, v.position.Y - 2 + offset.Y, 4, 4);
                
                // 制御点
                foreach (var c in mesh.mesh.CopyControlPoints())
                    g.FillRectangle(Brushes.Red, c.position.X - 2 + offset.X, c.position.Y - 2 + offset.Y, 4, 4);

                // 切り口
                if (sections != null)
                {
                    foreach (var r in sections)
                    {
                        for (int i = r.First; i < r.First + r.Length; i++)
                        {
                            int idx = mesh.mesh.pathIndices[FLib.FMath.Rem(i, mesh.mesh.pathIndices.Count)];
                            PointF p = mesh.mesh.vertices[idx].position;
                            g.FillRectangle(Brushes.Blue, p.X - 2 + offset.X, p.Y - 2 + offset.Y, 4, 4);
                        }
                    }
                }

                if (showPath)
                {
                    var path = GetPath(mesh);
                    foreach (var p in path)
                        g.FillRectangle(Brushes.Red, p.X - 5 + offset.X, p.Y - 5 + offset.Y, 10, 10);
                }
            }
            
            return bmp;
        }
        static List<PointF> GetPath(PatchSkeletalMesh mesh)
        {
            List<PointF> path = new List<PointF>();
            foreach (var i in mesh.mesh.pathIndices)
                path.Add(mesh.mesh.vertices[i].position);
            return path;
        }

        static PointF Offset(PointF pt, PointF offset)
        {
            return new PointF(pt.X + offset.X, pt.Y + offset.Y);
        }
    }
}
