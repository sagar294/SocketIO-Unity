using System.Collections;
using UnityEngine;
using SocketIO;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using System.Runtime.Serialization;
using UnityEngine.UI;
using System;
using System.Security.Cryptography;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;

public class SocketHandler : MonoBehaviour
{
	public static SocketHandler Inst;

	public SocketIOComponent socket;
	internal bool isPongReceived;
	int pingMissCounter;
	public bool isDebugLog;
	public bool isEncrypt;

	void Awake ()
	{
		Inst = this;
	}

	#region Socket Data

	IEnumerator Start ()
	{
		socket.url = GS.Inst._serverUrl;
		while (!GS.Inst.internetAvail && !GS.Inst.INTERNETAvail) {
			yield return new WaitForSecondsRealtime (1f);
		}
		StartCoroutine (VersionResponse ());
	}

	public IEnumerator VersionResponse ()
	{
		string URL = GS.Inst._versionUrl;
		using (WWW www = new WWW (URL)) {
			yield return www;
			string data = www.text;
			if (!string.IsNullOrEmpty (www.error)) {
				www.Dispose ();
				StartCoroutine (VersionResponse1 (URL));
				yield break;
			}
			www.Dispose ();
			JSONObject jdata = new JSONObject (data);
			DataSplit (jdata);
		}
	}

	private static bool TrustCertificate (object sender, X509Certificate x509Certificate, X509Chain x509Chain, SslPolicyErrors sslPolicyErrors)
	{
		return true;
	}

	IEnumerator VersionResponse1 (string URL)
	{
		yield return new WaitForSecondsRealtime (1f);
		string responseFromServer = "";
		ServicePointManager.ServerCertificateValidationCallback = TrustCertificate;
		HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create (URL);

		httpWebRequest.BeginGetResponse ((IAsyncResult result) => {
			HttpWebResponse response = (result.AsyncState as HttpWebRequest).EndGetResponse (result) as HttpWebResponse;
			if (response.StatusCode == HttpStatusCode.OK) {
				Stream dataStream = response.GetResponseStream ();
				StreamReader reader = new StreamReader (dataStream);
				responseFromServer = reader.ReadToEnd ();
			}
		}, httpWebRequest);

		while (responseFromServer == "")
			yield return 0;
		JSONObject jdata = new JSONObject (responseFromServer);
		DataSplit (jdata);
	}

	void DataSplit (JSONObject data)
	{
		float AV = float.Parse (data.GetField ("av").ToString ().Trim ('"'));
		float currentVersion = float.Parse (Application.version);
		if (currentVersion < AV) {
			PreLoader.Inst.DisablePreloader ();
			AlertUI.Inst.InitAlert ("Version Alert", "You are using old application version contact support team.\nDownload new version.");
		} else {
			GS.OpenScreenUI ("LoginUI");
			Init ();
		}
	}

	void Init ()
	{
		//Socket setup and connect
		socket.SetUpWS ();
		socket.Connect ();

		//Event Initialize
		socket.On ("open", TestOpen);
		socket.On ("error", TestError);
		socket.On ("close", TestClose);
		socket.On ("res", TestResponse);
		socket.On ("hb", TestPong);
		StartCoroutine (CheckPingPong ());
	}

	void Update ()
	{
		if (GS.Inst.isSocketDisconnect && !GS.Inst.onceTry) {
			GS.Inst.onceTry = true;
			GS.Inst.isReloginCalled = false;
			StartCoroutine (ReInitSocket ());
		}
	}

	internal bool isSocketConnected {
		get {
			return socket.IsWsConnected;
		}
	}

	public void TestOpen (SocketIOEvent e)
	{
		if (isDebugLog)
			print ("[SocketIO] TestOpen");
	}

	public void TestError (SocketIOEvent e)
	{
		if (isDebugLog)
			print ("[SocketIO] TestError");
		if (!GS.Inst.isReloginCalled)
			GS.Inst.isSocketDisconnect = true;
	}

	public void TestClose (SocketIOEvent e)
	{	
		if (isDebugLog)
			print ("[SocketIO] TestClose");
		if (!GS.Inst.isReloginCalled)
			GS.Inst.isSocketDisconnect = true;
	}

	public void TestResponse (SocketIOEvent e)
	{
		if (isEncrypt) {
			string recv = GS.Inst.AESDecrypt (e.data.GetField ("data").ToString ().Trim (new char[]{ '"' }));
			JSONObject obj = new JSONObject (recv);
			if (isDebugLog)
				print ("<color=green>Received: </color>" + obj.ToString ());
			GS.Inst.OnReceiveData (obj);
		} else {
			if (isDebugLog)
				print ("<color=green>Received: </color>" + e.data.ToString ());
			GS.Inst.OnReceiveData (e.data);
		} 
	}

	/// <summary>
	/// Tests the pong.
	/// </summary>
	/// <param name="e">E.</param>
	public void TestPong (SocketIOEvent e)
	{
		isPongReceived = true;
	}

	public void SendData (JSONObject obj)
	{
		if (isDebugLog)
			print ("<color=blue>[SocketIO] SEND : </color>" + obj.Print (true));
		
		if (isEncrypt) {
			JSONObject newData = new JSONObject ();
			string tempstr = GS.Inst.AESEncrypt (obj.Print ());
			newData.AddField ("data", tempstr);
			socket.Emit ("req", newData);
		} else {
			socket.Emit ("req", obj);
		}
	}

	/// <summary>
	/// Checks the ping pong.
	/// </summary>
	/// <returns>The ping pong.</returns>
	IEnumerator CheckPingPong ()
	{
		yield return new WaitForSecondsRealtime (3f);

		SB:
		if (isSocketConnected && GS.Inst.internetAvail) {
			isPongReceived = false;
			socket.Emit ("hb", new JSONObject ());
			float wait = 3;
			int c = 3;
			yield return new WaitForSecondsRealtime (wait);
			if (isPongReceived) {
				pingMissCounter = 0;
			} else {
				pingMissCounter++;
				if (pingMissCounter > c) {
					pingMissCounter = 0;
				}
			}
		} else
			yield return new WaitForSecondsRealtime (1);
		goto SB;
	}

	#endregion

	int _tryCount = 0;
	int _reconnectDelay = 8;

	IEnumerator ReInitSocket ()
	{
		_tryCount = 0;
		yield return new WaitForSecondsRealtime (1);

		SB:
		_tryCount++;
		if (GS.Inst.INTERNETAvail && GS.Inst.internetAvail) {
			if (_tryCount < _reconnectDelay) {
				yield return new WaitForSecondsRealtime (1f);
				goto SB;
			}
			if (isSocketConnected) {
				GS.Inst.isReloginCalled = true;
				ReLogin ();
			} else {
				socket.Connect ();
				yield return new WaitForSecondsRealtime (2f);
				goto SB;
			}
		} else {
			yield return new WaitForSecondsRealtime (1);
			goto SB;
		}
	}

	void ReLogin ()
	{
		if (!LoginUI.Inst.isLogin) {
			GS.Inst.onceTry = false;
			GS.Inst.isSocketDisconnect = false;
			GS.Inst.isReloginCalled = false;
			return;
		}
		LoginUI.Inst.LoginUser ();
	}

	void OnApplicationQuit ()
	{
		Close ();
	}

	void Close ()
	{
		if (isSocketConnected) {
			socket.sid = "";
			socket.Close ();
		}
	}

}
