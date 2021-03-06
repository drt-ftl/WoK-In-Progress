//---------------------------------------------
//            Tasharen Network
// Copyright © 2012-2015 Tasharen Entertainment
//---------------------------------------------

using System;
using System.IO;
using System.Net;
using System.Threading;

namespace TNet
{
/// <summary>
/// TCP-based lobby server link. Designed to communicate with a remote TcpLobbyServer.
/// You can use this class to register your game server with a remote lobby server.
/// </summary>

public class TcpLobbyServerLink : LobbyServerLink
{
	TcpProtocol mTcp;
	IPEndPoint mRemoteAddress;
	long mNextConnect = 0;
	bool mWasConnected = false;
	long mTimeDifference = 0;
	bool mUpdateNeeded = false;

	/// <summary>
	/// Create a new link to a remote lobby server.
	/// </summary>

	public TcpLobbyServerLink (IPEndPoint address) : base(null) { mRemoteAddress = address; }

	/// <summary>
	/// Whether the link is currently active.
	/// </summary>

	public override bool isActive { get { return mTcp.isConnected; } }

	/// <summary>
	/// Current time on the server.
	/// </summary>

	public long serverTime { get { return mTimeDifference + (System.DateTime.UtcNow.Ticks / 10000); } }

	/// <summary>
	/// Start the lobby server link.
	/// </summary>

	public override void Start ()
	{
		base.Start();

		if (mTcp == null)
		{
			mTcp = new TcpProtocol();
			mTcp.name = "Link";
		}
		mNextConnect = 0;
	}

	/// <summary>
	/// Send a server update.
	/// </summary>

	public override void SendUpdate (GameServer server)
	{
		if (!mShutdown)
		{
			mGameServer = server;
			mUpdateNeeded = true;

			if (mThread == null)
			{
				mThread = new Thread(ThreadFunction);
				mThread.Start();
			}
		}
	}

	/// <summary>
	/// Send periodic updates.
	/// </summary>

	void ThreadFunction()
	{
		mInternal = new IPEndPoint(Tools.localAddress, mGameServer.tcpPort);
		mExternal = new IPEndPoint(Tools.externalAddress, mGameServer.tcpPort);

		for (; ; )
		{
			long time = DateTime.UtcNow.Ticks / 10000;

			if (mShutdown)
			{
				mTcp.Disconnect();
				mThread = null;
				break;
			}

			Buffer buffer;

			// Try to establish a connection
			if (mGameServer != null && !mTcp.isConnected && mNextConnect < time)
			{
				mUpdateNeeded = true;
				mNextConnect = time + 15000;
				mTcp.Connect(mRemoteAddress);
			}
			
			while (mTcp.ReceivePacket(out buffer))
			{
				BinaryReader reader = buffer.BeginReading();
				Packet response = (Packet)reader.ReadByte();

				if (mTcp.stage == TcpProtocol.Stage.Verifying)
				{
					if (mTcp.VerifyResponseID(response, reader))
					{
						mTimeDifference = reader.ReadInt64() - (System.DateTime.UtcNow.Ticks / 10000);
						mWasConnected = true;
					}
					else
					{
#if STANDALONE
						Tools.Print("TcpLobbyLink: Protocol version mismatch");
#endif
						mThread = null;
						return;
					}
				}
				else
				{
					// Automatically try to re-establish a connection on disconnect
					mNextConnect = mWasConnected ? time + 1000 : time + 30000;
#if STANDALONE
					if (response == Packet.Error)
						Tools.Print("TcpLobbyLink: " + reader.ReadString());
					else if (response != Packet.Disconnect)
						Tools.Print("TcpLobbyLink can't handle this packet: " + response);
#endif
				}
				buffer.Recycle();
			}

			if (mGameServer != null && mUpdateNeeded && mTcp.isConnected)
			{
				mUpdateNeeded = false;
				BinaryWriter writer = mTcp.BeginSend(Packet.RequestAddServer);
				writer.Write(GameServer.gameID);
				writer.Write(mGameServer.name);
				writer.Write((short)mGameServer.playerCount);
				Tools.Serialize(writer, mInternal);
				Tools.Serialize(writer, mExternal);
				mTcp.EndSend();
			}
			Thread.Sleep(10);
		}
	}
}
}
