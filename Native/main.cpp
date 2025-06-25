#include <string>
#include <sstream>
#include <vector>
#include <Windows.h>

struct NativeStruct {
	std::string methodName;
	intptr_t resourceID;
};

template<typename T>
T ReadPrimitive(std::istream& stream) {
	T value;
	stream.read(reinterpret_cast<char*>(&value), sizeof(T));
	if (!stream) {
		throw std::runtime_error("Failed to read from stream.");
	}
	return value;
}

int Read7BitEncodedInt(std::istream& stream) {
	int count = 0;
	int shift = 0;
	uint8_t b;
	do {
		if (shift == 5 * 7) {
			throw std::runtime_error("Invalid 7-bit encoded int format.");
		}
		b = ReadPrimitive<uint8_t>(stream);
		count |= (b & 0x7F) << shift;
		shift += 7;
	} while ((b & 0x80) != 0);
	return count;
}

std::string ReadCSharpString(std::istream& stream) {
	int length = Read7BitEncodedInt(stream);
	if (length < 0) {
		throw std::runtime_error("Invalid string length read.");
	}
	if (length == 0) {
		return "";
	}
	std::vector<char> buffer(length);
	stream.read(buffer.data(), length);
	if (!stream) {
		throw std::runtime_error("Failed to read string bytes from stream.");
	}
	return std::string(buffer.begin(), buffer.end());
}

NativeStruct ReadNativeObjectInfo(std::istream& stream) {
	NativeStruct obj;
	obj.methodName = ReadCSharpString(stream);
	obj.resourceID = ReadPrimitive<intptr_t>(stream);
	return obj;
}

std::vector<NativeStruct> DeserializeNativeObjectInfoList(const std::vector<char>& data) {
	static_assert(sizeof(intptr_t) == 8, "This code assumes a 64-bit architecture to match C# nint.");

	std::istringstream memoryStream(std::string(data.begin(), data.end()), std::ios::binary);

	if (data.size() < sizeof(int32_t)) {
		return {};
	}

	int32_t count = ReadPrimitive<int32_t>(memoryStream);

	std::vector<NativeStruct> list;
	list.reserve(count);

	for (int32_t i = 0; i < count; ++i) {
		list.push_back(ReadNativeObjectInfo(memoryStream));
	}

	return list;
}

uint8_t* GetResourceByID(intptr_t id, size_t* size) {
	if (id == 0) {
		*size = 0;
		return nullptr;
	}

	HMODULE hModule = GetModuleHandle("Native.dll");
	HRSRC hRs = FindResource(hModule, MAKEINTRESOURCE(id), RT_RCDATA);
	if (hRs == NULL) {
		*size = 0;
		return nullptr;
	}
	size_t resSize = SizeofResource(hModule, hRs);
	if (resSize == 0) {
		*size = 0;
		return nullptr;
	}
	HGLOBAL hGl = LoadResource(hModule, hRs);
	LPVOID pData = LockResource(hGl);
	if (pData == NULL) {
		*size = 0;
		return nullptr;
	}

	uint8_t* rData = new uint8_t[resSize];
	memcpy(rData, pData, resSize);
	*size = resSize;

	return rData;
}

extern "C" {
	__declspec(dllexport) uint8_t* GetByteAssembly(const char* methodName, size_t* size) {
		if (methodName == nullptr || size == nullptr) {
			if (size) *size = 0;
			return nullptr;
		}

		intptr_t rID = 0;
		size_t tSize = 0;

		uint8_t* methodTableData = GetResourceByID(103, &tSize);
		if (methodTableData == nullptr) {
			*size = 0;
			return nullptr;
		}

		const char* table = reinterpret_cast<const char*>(methodTableData);
		std::vector<char> methodTable(table, table + tSize);
		std::vector<NativeStruct> nativeObjects = DeserializeNativeObjectInfoList(methodTable);

		delete[] methodTableData;

		for (const auto& object : nativeObjects) {
			if (object.methodName == methodName) {
				rID = object.resourceID;
				break;
			}
		}

		return GetResourceByID(rID, size);
	}

	__declspec(dllexport) void FreeByteAssembly(uint8_t* buffer) {
		delete[] buffer;
	}
}