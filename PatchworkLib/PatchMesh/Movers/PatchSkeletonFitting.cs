using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Drawing.Drawing2D;
using FLib;

namespace PatchworkLib.PatchMesh
{
    public class PatchSkeletonFitting
    {
        public static void Fitting(PatchSkeletalMesh smesh, PatchSkeleton skl)
        {
            FTimer.Resume("Fitting:SetCtrl");
            // スケルトンに合わせて制御点を移動
            foreach (var kv in smesh.skeletalControlPointDict)
            {
                PatchSkeletonBone b = kv.Key;
                List<PointF> orgPts = kv.Value;
                PatchSkeletonBone br = CorrespondingBone(skl, b);

                if (br == null)
                    continue;
                if (orgPts.Count <= 1)
                    continue;

                for (int i = 0; i < orgPts.Count; i++)
                {
                    float t = (float)i / (orgPts.Count - 1);

                    // smesh.skeletalControlPointExpandToXXXの値にしたがって、src, dst方向にそれぞれ伸長させる
                    float exSrc = smesh.endJoints.Contains(b.src.name) ? smesh.srcStretchRatio : 0;
                    float exDst = smesh.endJoints.Contains(b.dst.name) ? smesh.dstStretchRatio : 0;
                    float dx = br.dst.position.X - br.src.position.X;
                    float dy = br.dst.position.Y - br.src.position.Y;
                    t *= 1 + exSrc + exDst;
                    float lx = dx * t;
                    float ly = dy * t;
                    float ox = -dx * exSrc;
                    float oy = -dy * exSrc;
                    float x = br.src.position.X + ox + lx;
                    float y = br.src.position.Y + oy + ly;
                    
//                    float x = br.src.position.X * (1 - t) + br.dst.position.X * t;
//                    float y = br.src.position.Y * (1 - t) + br.dst.position.Y * t;

                    PatchControlPoint c = smesh.mesh.FindControlPoint(orgPts[i]);
                    if (c == null)
                        continue;
                    smesh.mesh.TranslateControlPoint(c.position, new PointF(x, y), false);
                }

                // スケルトン(mesh.skl)も動かす
                b.src.position = br.src.position;
                b.dst.position = br.dst.position;
            
            }
            FTimer.Pause("Fitting:SetCtrl");

            // 制御点の移動を反映してメッシュ変形
            FTimer.Resume("Fitting:FlushDefomation");
            smesh.mesh.FlushDefomation();
            FTimer.Pause("Fitting:FlushDefomation");


        }

        // スケルトン内のbと同じ（jointの名前が同じ）ボーンを探す
        static PatchSkeletonBone CorrespondingBone(PatchSkeleton skl, PatchSkeletonBone b)
        {
            if (skl == null || b == null || skl.bones == null)
                return null;
            foreach (var bb in skl.bones)
                // オーバーライドしてるjointの名前が同じならtrue
                if (b == bb)
                    return bb;
            return null;
        }
    }
}
