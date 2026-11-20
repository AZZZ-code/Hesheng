#include "gui/bridge_service.h"
#include "processor.h"
#include <windows.h>
#include <algorithm>
#include <chrono>
#include <cmath>
#include <cwchar>
#include <string>
#include <vector>
std::atomic<bool> g_keepRunning{true};
RecoveryState g_recoveryState;
std::mutex g_wakeupMutex;
std::condition_variable g_wakeupCv;
static wb::BridgeService bridge;
#define API extern "C" __declspec(dllexport)
struct Output { std::wstring id,name; bool preferred; };
static std::vector<Output> devices;
static std::wstring wide(const char* str){
    int n=MultiByteToWideChar(CP_UTF8,0,str,-1,nullptr,0);
    if(n<1)return L"";
    std::wstring result(n,L'\0');MultiByteToWideChar(CP_UTF8,0,str,-1,result.data(),n);result.pop_back();return result;
}
API int scan(){
    devices.clear();ma_context ctx{};ma_backend backend=ma_backend_wasapi;
    if(ma_context_init(&backend,1,nullptr,&ctx)!=MA_SUCCESS)return -1;
    ma_device_info* list=nullptr;ma_uint32 count=0;
    auto result=ma_context_get_devices(&ctx,&list,&count,nullptr,nullptr);
    if(result==MA_SUCCESS)for(unsigned i=0;i<count;i++)devices.push_back({list[i].id.wasapi,wide(list[i].name),list[i].isDefault!=0});
    ma_context_uninit(&ctx);return result==MA_SUCCESS?int(devices.size()):-1;
}
API const wchar_t* device_id(int i){return devices.at(i).id.c_str();}
API const wchar_t* device_name(int i){return devices.at(i).name.c_str();}
API int is_default(int i){return devices.at(i).preferred;}
static bool copyId(ma_device_id& out,const wchar_t* id){
    if(!id||!*id||wcslen(id)>=std::size(out.wasapi))return false;
    wcscpy(out.wasapi,id);return true;
}
API int begin_route(const wchar_t* source,const wchar_t* target){
    BridgeConfig cfg{};
    if(!copyId(cfg.sourceDeviceId,source)||!copyId(cfg.targetDeviceId,target)||!_wcsicmp(source,target))return 0;
    cfg.shareMode=ma_share_mode_shared;cfg.targetLatency=10;
    return bridge.Start(cfg);
}
API void stop_route(){bridge.RequestStop();}
API int state(){return int(bridge.State());}
API int shutdown_route(){return bridge.TryGracefulStop(3500);}
API float take_peak(){return tone.peak.exchange(0);}
API int sample_rate(){return tone.sampleRate.load();}
API void tune(const float* bands,float preamp,int enabled,int delay){
    for(int i=0;i<5;i++)tone.bands[i].store(std::clamp(bands[i],-12.f,12.f));
    tone.preamp.store(std::clamp(preamp,-18.f,0.f));tone.enabled.store(enabled!=0);tone.delayMs.store(std::clamp(delay,0,300));
}
static void chime(ma_device* device,void* output,const void*,ma_uint32 frames){
    auto& index=*static_cast<unsigned*>(device->pUserData);
    auto* dst=static_cast<float*>(output);
    const unsigned rate=device->sampleRate;
    for(unsigned f=0;f<frames;f++,index++){
        const unsigned beat=index/(rate/3);
        const double t=double(index%(rate/3))/rate;
        const double envelope=std::clamp(std::min(t*100,(.23-t)*35),0.,1.);
        const double hz=beat==0?653:beat==1?1031:1499;
        float v=beat<3?float(.04*envelope*std::sin(6.283185307179586*hz*t)):0;
        for(unsigned c=0;c<device->playback.channels;c++)*dst++=v;
    }
}
API int play_chime(const wchar_t* source){
    ma_device_id id{};if(!copyId(id,source))return 0;
    unsigned position=0;auto cfg=ma_device_config_init(ma_device_type_playback);
    cfg.playback.format=ma_format_f32;cfg.playback.channels=2;cfg.playback.pDeviceID=&id;cfg.dataCallback=chime;cfg.pUserData=&position;
    ma_device dev{};if(ma_device_init(nullptr,&cfg,&dev)!=MA_SUCCESS)return 0;
    bool ok=ma_device_start(&dev)==MA_SUCCESS;
    if(ok)std::this_thread::sleep_for(std::chrono::milliseconds(1250));
    ma_device_uninit(&dev);return ok;
}
