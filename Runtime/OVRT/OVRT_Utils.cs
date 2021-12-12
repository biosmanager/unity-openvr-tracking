using UnityEngine;
using Valve.VR;

namespace OVRT
{
    public static class OVRT_Utils
    {
		[System.Serializable]
		public struct RigidTransform
		{
			public Vector3 pos;
			public Quaternion rot;

			public static RigidTransform identity
			{
				get { return new RigidTransform(Vector3.zero, Quaternion.identity); }
			}

			public static RigidTransform FromLocal(Transform t)
			{
				return new RigidTransform(t.localPosition, t.localRotation);
			}

			public RigidTransform(Vector3 pos, Quaternion rot)
			{
				this.pos = pos;
				this.rot = rot;
			}

			public RigidTransform(Transform t)
			{
				this.pos = t.position;
				this.rot = t.rotation;
			}

			public RigidTransform(Transform from, Transform to)
			{
				var inv = Quaternion.Inverse(from.rotation);
				rot = inv * to.rotation;
				pos = inv * (to.position - from.position);
			}

			public RigidTransform(HmdMatrix34_t pose)
			{
				var m = Matrix4x4.identity;

				m[0, 0] = pose.m0;
				m[0, 1] = pose.m1;
				m[0, 2] = -pose.m2;
				m[0, 3] = pose.m3;

				m[1, 0] = pose.m4;
				m[1, 1] = pose.m5;
				m[1, 2] = -pose.m6;
				m[1, 3] = pose.m7;

				m[2, 0] = -pose.m8;
				m[2, 1] = -pose.m9;
				m[2, 2] = pose.m10;
				m[2, 3] = -pose.m11;

				this.pos = m.GetPosition();
				this.rot = m.GetRotation();
			}

			public RigidTransform(HmdMatrix44_t pose)
			{
				var m = Matrix4x4.identity;

				m[0, 0] = pose.m0;
				m[0, 1] = pose.m1;
				m[0, 2] = -pose.m2;
				m[0, 3] = pose.m3;

				m[1, 0] = pose.m4;
				m[1, 1] = pose.m5;
				m[1, 2] = -pose.m6;
				m[1, 3] = pose.m7;

				m[2, 0] = -pose.m8;
				m[2, 1] = -pose.m9;
				m[2, 2] = pose.m10;
				m[2, 3] = -pose.m11;

				m[3, 0] = pose.m12;
				m[3, 1] = pose.m13;
				m[3, 2] = -pose.m14;
				m[3, 3] = pose.m15;

				this.pos = m.GetPosition();
				this.rot = m.GetRotation();
			}

			public HmdMatrix44_t ToHmdMatrix44()
			{
				var m = Matrix4x4.TRS(pos, rot, Vector3.one);
				var pose = new HmdMatrix44_t();

				pose.m0 = m[0, 0];
				pose.m1 = m[0, 1];
				pose.m2 = -m[0, 2];
				pose.m3 = m[0, 3];

				pose.m4 = m[1, 0];
				pose.m5 = m[1, 1];
				pose.m6 = -m[1, 2];
				pose.m7 = m[1, 3];

				pose.m8 = -m[2, 0];
				pose.m9 = -m[2, 1];
				pose.m10 = m[2, 2];
				pose.m11 = -m[2, 3];

				pose.m12 = m[3, 0];
				pose.m13 = m[3, 1];
				pose.m14 = -m[3, 2];
				pose.m15 = m[3, 3];

				return pose;
			}

			public HmdMatrix34_t ToHmdMatrix34()
			{
				var m = Matrix4x4.TRS(pos, rot, Vector3.one);
				var pose = new HmdMatrix34_t();

				pose.m0 = m[0, 0];
				pose.m1 = m[0, 1];
				pose.m2 = -m[0, 2];
				pose.m3 = m[0, 3];

				pose.m4 = m[1, 0];
				pose.m5 = m[1, 1];
				pose.m6 = -m[1, 2];
				pose.m7 = m[1, 3];

				pose.m8 = -m[2, 0];
				pose.m9 = -m[2, 1];
				pose.m10 = m[2, 2];
				pose.m11 = -m[2, 3];

				return pose;
			}

			public override bool Equals(object o)
			{
				if (o is RigidTransform)
				{
					RigidTransform t = (RigidTransform)o;
					return pos == t.pos && rot == t.rot;
				}
				return false;
			}



			public override int GetHashCode()
			{
				return pos.GetHashCode() ^ rot.GetHashCode();
			}

			public static bool operator ==(RigidTransform a, RigidTransform b)
			{
				return a.pos == b.pos && a.rot == b.rot;
			}

			public static bool operator !=(RigidTransform a, RigidTransform b)
			{
				return a.pos != b.pos || a.rot != b.rot;
			}

			public static RigidTransform operator *(RigidTransform a, RigidTransform b)
			{
				return new RigidTransform
				{
					rot = a.rot * b.rot,
					pos = a.pos + a.rot * b.pos
				};
			}

			public void Inverse()
			{
				rot = Quaternion.Inverse(rot);
				pos = -(rot * pos);
			}

			public RigidTransform GetInverse()
			{
				var t = new RigidTransform(pos, rot);
				t.Inverse();
				return t;
			}

			public void Multiply(RigidTransform a, RigidTransform b)
			{
				rot = a.rot * b.rot;
				pos = a.pos + a.rot * b.pos;
			}

			public Vector3 InverseTransformPoint(Vector3 point)
			{
				return Quaternion.Inverse(rot) * (point - pos);
			}

			public Vector3 TransformPoint(Vector3 point)
			{
				return pos + (rot * point);
			}

			public static Vector3 operator *(RigidTransform t, Vector3 v)
			{
				return t.TransformPoint(v);
			}

			public static RigidTransform Interpolate(RigidTransform a, RigidTransform b, float t)
			{
				return new RigidTransform(Vector3.Lerp(a.pos, b.pos, t), Quaternion.Slerp(a.rot, b.rot, t));
			}

			public void Interpolate(RigidTransform to, float t)
			{
				pos = SteamVR_Utils.Lerp(pos, to.pos, t);
				rot = SteamVR_Utils.Slerp(rot, to.rot, t);
			}
		}
	}
}