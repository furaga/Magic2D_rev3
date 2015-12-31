using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
namespace PatchworkLib.PatchMesh
{
    public static class PatchSkeletonFKMover
    {
        // 超簡易的なFK
        // 子ノードを親ノードと同じだけ平行移動させる
        public static void MoveJoint(PatchSkeleton skl, PatchSkeletonJoint joint, PointF to)
        {
            if (skl == null)
                return;
            if (joint == null)
                return;
            float dx = to.X - joint.position.X;
            float dy = to.Y - joint.position.Y;

            joint.position = to;

            HashSet<PatchSkeletonJoint> haschecked = new HashSet<PatchSkeletonJoint>();
            haschecked.Add(joint);

            MoveJoint(skl, skl.bones.Where(b => b.src == joint).Select(b => b.dst).ToList(), dx, dy, haschecked);
        }

        static void MoveJoint(PatchSkeleton skl, List<PatchSkeletonJoint> joints, float dx, float dy, HashSet<PatchSkeletonJoint> haschecked)
        {
            if (joints.Count <= 0)
                return;

            foreach (var j in joints)
            {
                j.position = new PointF(j.position.X + dx, j.position.Y + dy);
                haschecked.Add(j);

                MoveJoint(skl, skl.bones.Where(b => b.src == j).Select(b => b.dst).ToList(), dx, dy, haschecked);
            }
        }

    }
}
