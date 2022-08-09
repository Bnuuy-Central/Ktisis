﻿using System;
using System.Numerics;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using ImGuiNET;
using ImGuizmoNET;

using Dalamud.Logging;
using Dalamud.Game.Gui;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;

using FFXIVClientStructs.FFXIV.Client.Game.Control;

using Ktisis.Structs;
using Ktisis.Structs.Actor;
using Ktisis.Structs.Ktisis;
using Ktisis.Structs.FFXIV;

namespace Ktisis.Overlay {
	public sealed class SkeletonEditor {
		public GameGui Gui;
		public ObjectTable ObjectTable;

		public GameObject? Subject;
		public List<BoneList>? Skeleton;

		public (int, int) BoneSelection;
		public SharpDX.Matrix BoneMatrix;
		public Vector4 BoneTransform;
		public Vector3 BonePosition;

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		internal delegate IntPtr GetMatrixDelegate();
		internal GetMatrixDelegate GetMatrix;

		float[] cameraView = {
			1.0f, 0.0f, 0.0f, 0.0f,
			0.0f, 1.0f, 0.0f, 0.0f,
			0.0f, 0.0f, 1.0f, 0.0f,
			0.0f, 0.0f, 0.0f, 1.0f
		};

		public unsafe SkeletonEditor(Ktisis plugin, GameObject? subject) {
			Gui = plugin.GameGui;
			ObjectTable = plugin.ObjectTable;

			Subject = subject;
			BoneSelection = (-1, -1);

			var matrixAddr = plugin.SigScanner.ScanText("E8 ?? ?? ?? ?? 48 8D 4C 24 ?? 48 89 4c 24 ?? 4C 8D 4D ?? 4C 8D 44 24 ??");
			GetMatrix = Marshal.GetDelegateForFunctionPointer<GetMatrixDelegate>(matrixAddr);
		}

		public unsafe ActorModel* GetSubjectModel() {
			return ((Actor*)Subject?.Address)->Model;
		}

		public unsafe void BuildSkeleton() {
			Skeleton = new List<BoneList>();

			var model = GetSubjectModel();
			if (model == null)
				return;

			var list = *model->HkaIndex;
			for (int i = 0; i < list.Count; i++) {
				var index = list[i];
				if (index.Pose == null)
					continue;

				var bones = new BoneList(i, index.Pose);
				Skeleton.Add(bones);
			}
		}

		public unsafe void Draw(ImDrawListPtr draw) {
			var tarSys = TargetSystem.Instance();
			if (tarSys == null)
				return;

			var target = ObjectTable.CreateObjectReference((IntPtr)(tarSys->GPoseTarget));
			if (target == null || Subject == null || Subject.Address != target.Address) {
				Subject = target;
				if (Subject != null)
					BuildSkeleton();
			}

			if (Subject == null)
				return;
			if (Skeleton == null)
				return;

			var model = GetSubjectModel();
			if (model == null)
				return;

			var cam = CameraManager.Instance()->Camera;
			if (cam == null)
				return;

			var hasBoneHovered = false;

			foreach (BoneList bones in Skeleton) {
				foreach (Bone bone in bones) {
					var pair = (bones.Id, bone.Index);

					var boneTranslate = bone.Rotate(model->Rotation) * model->Height;

					var worldPos = Subject.Position + boneTranslate;
					Gui.WorldToScreen(worldPos, out var pos);

					if (bone.ParentId > 0) { // Lines
						var parent = bones.GetParentOf(bone);
						var parentPos = Subject.Position + parent.Rotate(model->Rotation) * model->Height;

						Gui.WorldToScreen(parentPos, out var pPos);
						draw.AddLine(pos, pPos, 0xffffffff);
					}

					if (pair == BoneSelection) { // Gizmo
						var io = ImGui.GetIO();
						var wp = ImGui.GetWindowPos();

						var matrix = (WorldMatrix*)GetMatrix();
						if (matrix == null)
							return;

						ImGuizmo.BeginFrame();
						ImGuizmo.SetDrawlist();
						ImGuizmo.SetRect(wp.X, wp.Y, io.DisplaySize.X, io.DisplaySize.Y);
						ImGuizmo.Manipulate(ref cameraView[0], ref matrix->Projection.M11, OPERATION.TRANSLATE, MODE.WORLD, ref BoneMatrix.M11);

						var t = bone.Transform;
						ImGuizmo.DecomposeMatrixToComponents(ref BoneMatrix.M11, ref t.Translate.X, ref t.Rotate.X, ref t.Scale.X);

						var delta = BonePosition - new Vector3(t.Translate.X, t.Translate.Y, t.Translate.Z);
						PluginLog.Information("{0}", delta);

						bone.Transform.Translate = BoneTransform - new Vector4(delta, 0.0f);
						bones.Transforms[bone.Index] = bone.Transform;
					} else { // Dot
						var radius = Math.Max(3.0f, 10.0f - cam->Distance);

						var area = new Vector2(radius, radius);
						var rectMin = pos - area;
						var rectMax = pos + area;

						var hovered = ImGui.IsMouseHoveringRect(rectMin, rectMax);
						if (hovered) {
							hasBoneHovered = true;
							if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
								BoneSelection = (bones.Id, bone.Index);
								BoneTransform = bone.Transform.Translate;
								BonePosition = worldPos;

								ImGuizmo.RecomposeMatrixFromComponents(
									ref worldPos.X,
									ref bone.Transform.Rotate.X,
									ref bone.Transform.Scale.X,
									ref BoneMatrix.M11
								);
							}
						}

						draw.AddCircleFilled(pos, Math.Max(3.0f, 10.0f - cam->Distance), hovered ? 0xffffffff : 0xb0ffffff, 100);
					}
				}
			}

			if (hasBoneHovered)
				ImGui.SetNextFrameWantCaptureMouse(hasBoneHovered);
		}
	}
}