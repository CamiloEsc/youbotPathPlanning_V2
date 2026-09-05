#pragma once

using namespace System;
using namespace System::Text;
using namespace System::Runtime::InteropServices;

namespace VRepAdapter {

	public ref class Helper
	{
	public:
		static IntPtr NativeFromString(String^ managedString) {
			int len = Encoding::ASCII->GetByteCount(managedString);
			array<Byte>^ buffer = gcnew array<Byte>(len + 1);
			Encoding::ASCII->GetBytes(managedString, 0, managedString->Length, buffer, 0);
			IntPtr nativeString = Marshal::AllocHGlobal(buffer->Length);
			Marshal::Copy(buffer, 0, nativeString, buffer->Length);
			return nativeString;
		}

		static String^ StringFromNative(IntPtr nativeString) {
			int len = 0;
			while (Marshal::ReadByte(nativeString, len) != 0) ++len;
			if (len == 0) return String::Empty;
			array<Byte>^ buffer = gcnew array<Byte>(len);
			Marshal::Copy(nativeString, buffer, 0, buffer->Length);
			return Encoding::ASCII->GetString(buffer);
		}
	};
}
