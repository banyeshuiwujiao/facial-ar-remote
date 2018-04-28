﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class Server : MonoBehaviour
{
	const int k_BufferPrewarm = 16;
	const int k_MaxBufferQueue = 1024; // No use in bufferring really old frames
	const int poseOffset = Client.BlendshapeSize + 1;
	const int cameraPoseOffset = poseOffset + Client.PoseSize;
	const int frameNumOffset = cameraPoseOffset + Client.PoseSize;
	static readonly Quaternion k_RotationOffset = Quaternion.AngleAxis(180, Vector3.up);

	[SerializeField]
	int m_Port = 9000;

	[Range(0.1f, 1)]
	[SerializeField]
	float m_CameraSmoothing = 0.8f;

	[Range(0.1f, 1)]
	[SerializeField]
	float m_FaceSmoothing = 0.8f;

	[SerializeField]
	SkinnedMeshRenderer m_SkinnedMeshRenderer;

	[SerializeField]
	Transform m_FaceTransform;

	Socket m_Socket;
	readonly float[] m_Blendshapes = new float[BlendshapeDriver.BlendshapeCount];
	GameObject m_FaceGameObject;
	Pose m_Pose;
	Pose m_CameraPose;
	Transform m_CameraTransform;
	bool m_Active;
	bool m_Running;
	int m_LastFrameNum;

	readonly Queue<byte[]> m_BufferQueue = new Queue<byte[]>(k_BufferPrewarm);
	readonly Queue<byte[]> m_UnusedBuffers = new Queue<byte[]>(k_BufferPrewarm);

	void Start()
	{
		Application.targetFrameRate = 60;
		for (var i = 0; i < k_BufferPrewarm; i++)
		{
			m_UnusedBuffers.Enqueue(new byte[Client.BufferSize]);
		}

		m_FaceGameObject = m_FaceTransform.gameObject;
		m_CameraTransform = Camera.main.transform;
		Debug.Log("Possible IP addresses:");
		foreach (var address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
		{
			Debug.Log(address);

			var endPoint = new IPEndPoint(address, m_Port);
			m_Socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
			m_Socket.Bind(endPoint);
			m_Socket.Listen(100);
			m_Running = true;
			m_LastFrameNum = -1;
			new Thread(() =>
			{
				m_Socket = m_Socket.Accept();
				Debug.Log("Client connected on " + address);

				var frameNumArray = new int[1];

				while (m_Running)
				{
					if (m_Socket.Connected)
					{
						try
						{
							var buffer = m_UnusedBuffers.Count == 0 ? new byte[Client.BufferSize] : m_UnusedBuffers.Dequeue();
							m_Socket.Receive(buffer);
							if (buffer[0] == Client.ErrorCheck)
							{
								m_BufferQueue.Enqueue(buffer);
								Buffer.BlockCopy(buffer, frameNumOffset, frameNumArray, 0, sizeof(int));

								var frameNum = frameNumArray[0];
								if (m_LastFrameNum != frameNum - 1)
									Debug.LogFormat("Dropped frame {0} (last frame: {1}) ", frameNum, m_LastFrameNum);

								m_LastFrameNum = frameNum;
							}
						}
						catch (Exception e)
						{
							Debug.LogError(e.Message);
						}
					}

					if (m_BufferQueue.Count > k_MaxBufferQueue)
						m_BufferQueue.Dequeue();

					Thread.Sleep(1);
				}
			}).Start();
		}
	}

	static void ArrayToPose(float[] poseArray, ref Pose pose)
	{
		pose.position = new Vector3(poseArray[0], poseArray[1], poseArray[2]);
		pose.rotation = new Quaternion(poseArray[3], poseArray[4], poseArray[5], poseArray[6]);
	}

	bool DequeueBuffer()
	{
		if (m_BufferQueue.Count == 0)
			return false;

		if (m_BufferQueue.Count > 2)
			m_BufferQueue.Dequeue(); // Throw out an old frame

		var poseArray = new float[7];
		var cameraPoseArray = new float[7];
		var buffer = m_BufferQueue.Dequeue();
		Buffer.BlockCopy(buffer, 1, m_Blendshapes, 0, Client.BlendshapeSize);
		Buffer.BlockCopy(buffer, poseOffset, poseArray, 0, Client.PoseSize);
		Buffer.BlockCopy(buffer, cameraPoseOffset, cameraPoseArray, 0, Client.PoseSize);
		ArrayToPose(poseArray, ref m_Pose);
		ArrayToPose(cameraPoseArray, ref m_CameraPose);
		m_Active = buffer[buffer.Length - 1] == 1;
		m_UnusedBuffers.Enqueue(buffer);

		return true;
	}

	void Update()
	{
		if (!DequeueBuffer())
			return;

		m_FaceGameObject.SetActive(m_Active);
		m_CameraTransform.localPosition = Vector3.Lerp(m_CameraTransform.localPosition, m_CameraPose.position, m_CameraSmoothing);
		m_CameraTransform.localRotation = Quaternion.Lerp(m_CameraTransform.localRotation, m_CameraPose.rotation, m_CameraSmoothing);
		m_FaceTransform.localPosition = Vector3.Lerp(m_FaceTransform.localPosition, m_Pose.position, m_FaceSmoothing);
		m_FaceTransform.localRotation = Quaternion.Lerp(m_FaceTransform.localRotation, m_Pose.rotation * k_RotationOffset, m_FaceSmoothing);
		for (var i = 0; i < BlendshapeDriver.BlendshapeCount; i++)
		{
			m_SkinnedMeshRenderer.SetBlendShapeWeight(i, m_Blendshapes[i] * 100);
		}
	}

	void OnDestroy()
	{
		m_Running = false;
	}
}
