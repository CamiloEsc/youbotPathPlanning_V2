// VRepAdapter.h

#pragma once

using namespace System;

#include "Helper.h"

#ifndef _EXTAPI__
#include "extApi.h"
#endif

namespace VRepAdapter {

	public ref class VRepFunctions
	{
	public:
		static String^ GetStringSignal(int ClientID, String^ signalName)
		{
			//getting native string from managed C# string
			simxChar* name = (simxChar*)Helper::NativeFromString(signalName).ToPointer();

			simxUChar* pBuffer = extApi_allocateBuffer(100000);//a buffer for the string that comes from V-REP


			int len = 0; //recieved string length (typically 7000 bytes - smaller than buffer size)


			// HERE COMES THE ERROR (See the screenshot) 
			// specifically at extApiPlatform.cpp, line 254, in the function 'extApi_releaseBuffer'
			simxInt res = simxGetStringSignal(ClientID, name, &pBuffer, &len, simx_opmode_streaming); \

				String^ str = "";

			if (len > 0)
			{
				pBuffer[len - 1] = '\0'; //string termination
				//converting native string to C# string
				IntPtr pt((void*)pBuffer);
				str = Helper::StringFromNative(pt);
			}
			//extApi_releaseBuffer(pBuffer); //called automatically


			return str;
		}

		static int Start(String ^ip, int port)
		{
			simxChar* z = (simxChar*)Helper::NativeFromString(ip).ToPointer();
			int clientID = simxStart(z, port, true, true, 2000, 5);
			return clientID;
		}

		static int GetConnectionId(int clientID)
		{
			return simxGetConnectionId(clientID);
		}
		static int ReadProximitySensor(int clientID, int sensorHandle, [Out] Byte% sensorTrigger)
		{
			simxUChar sensorTrigger_ = 0;
			int res = simxReadProximitySensor(clientID, sensorHandle, &sensorTrigger_, NULL, NULL, NULL, simx_opmode_continuous);
			sensorTrigger = sensorTrigger_;
			return res;
		}
		static void SetJointTargetVelocity(int clientID, int sensorHandle, float motorSpeed)
		{
			simxSetJointTargetVelocity(clientID, sensorHandle, motorSpeed, simx_opmode_oneshot);
		}

		static void sleepMs(int ms)
		{
			extApi_sleepMs(ms);
		}

		static void Finish(int clientID)
		{
			simxFinish(clientID);
		}
		static int GetObjectHandle(int clientID, String^ name, [Out] int% handle)
		{
			simxChar* z = (simxChar*)Helper::NativeFromString(name).ToPointer();
			int handle_ = 0;
			int res = simxGetObjectHandle(clientID, z, &handle_, simx_opmode_oneshot_wait);
			handle = handle_;
			return res;
		}

		static int GetLastCmdTime(int clientID)
		{
			return simxGetLastCmdTime(clientID);
		}

	};
}
