#include <windows.h>
#include <iostream>

int main() {
    // 1. 加载 mscoree.dll  
    HMODULE hModule = LoadLibrary(TEXT("mscoree.dll"));

    if (hModule == NULL) {
        // 获取错误信息 
        DWORD error = GetLastError();
        std::cerr << "加载 mscoree.dll  失败，错误代码: " << error << std::endl;
        return 1;
    }

    // 2. 成功加载后的操作（示例）
    std::cout << "mscoree.dll  加载成功！模块句柄: " << hModule << std::endl;

    // 3. 可选：获取函数指针（以 CLRCreateInstance 为例）
    FARPROC pFunc = GetProcAddress(hModule, "CLRCreateInstance");
    if (pFunc != NULL) {
        std::cout << "找到 CLRCreateInstance 函数地址: " << pFunc << std::endl;
    }
    else {
        std::cerr << "未找到指定函数。" << std::endl;
    }

    // 4. 释放库（根据实际需求决定是否立即释放）
    FreeLibrary(hModule);
    return 0;
}