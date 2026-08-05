// Fake aitalked.dll - a test double that pretends to be the AITalk SDK.
//
// Used to verify the C# P/Invoke contract of VOICEROID2-YMM (struct layout,
// calling convention, callbacks, event-driven job completion) without a real
// VOICEROID2 installation. It uses the genuine struct definitions from
// aitalk_AITalk.h and exports the same function names as aitalk_wrapper.
//
// Behavior:
//   - Init: logs its arguments to the path in env var AITALK_FAKE_LOG
//   - TextToKana: returns "[H]<input text>[K]" via callbacks
//   - TextToSpeech: returns 4410 samples (0.1 s) of a ramp wave via callbacks
//   - GetParam / SetParam: keeps parameters and logs speaker values
//   - Dictionary Reload: SUCCESS if the file exists, else FILE_NOT_FOUND

#include "aitalk_AITalk.h"
#include <windows.h>

#include <chrono>
#include <cstddef>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

static std::mutex g_logMutex;
static std::string g_logPath;

static void logLine(const std::string &line)
{
    std::lock_guard<std::mutex> lock(g_logMutex);
    if (g_logPath.empty())
        return;
    FILE *f = fopen(g_logPath.c_str(), "a");
    if (!f)
        return;
    fprintf(f, "%s\n", line.c_str());
    fclose(f);
}

static std::string ptrStr(const char *p)
{
    return p ? std::string(p) : std::string("(null)");
}

static std::string fmtFloat(float v)
{
    char buf[64];
    snprintf(buf, sizeof(buf), "%.4g", (double)v);
    return std::string(buf);
}

static bool fileExists(const char *path)
{
    if (!path)
        return false;
    DWORD attributes = GetFileAttributesA(path);
    return attributes != INVALID_FILE_ATTRIBUTES;
}

// Current parameters (updated by SetParam, returned by GetParam)
static AITalk_TTtsParam g_param = {};
static bool g_hasParam = false;
static std::string g_voiceName;
static int g_jobCounter = 0;

// Payload for a single job (the engine serializes jobs)
struct JobData
{
    int jobId = 0;
    std::string kana;
    std::vector<short> samples;
    bool kanaConsumed = false;
    bool samplesConsumed = false;
};
static JobData g_job;

static void textBufThread(AITalkProcTextBuf proc, int jobId)
{
    std::this_thread::sleep_for(std::chrono::milliseconds(50));
    proc(AITALKEVENT_TEXTBUF_FULL, jobId, nullptr);
    proc(AITALKEVENT_TEXTBUF_CLOSE, jobId, nullptr);
}

static void rawBufThread(AITalkProcRawBuf proc, int jobId)
{
    std::this_thread::sleep_for(std::chrono::milliseconds(50));
    proc(AITALKEVENT_RAWBUF_FULL, jobId, 0, nullptr);
    proc(AITALKEVENT_RAWBUF_CLOSE, jobId, 0, nullptr);
}

BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        const char *p = getenv("AITALK_FAKE_LOG");
        if (p)
            g_logPath = p;
    }
    return TRUE;
}

extern "C"
{

__declspec(dllexport) AITalkResultCode AITalkAPI_Init(AITalk_TConfig *config)
{
    logLine("init hz=" + std::to_string(config->hzVoiceDB)
        + " voiceDbs=" + ptrStr(config->dirVoiceDBS)
        + " license=" + ptrStr(config->pathLicense)
        + " seed=" + ptrStr(config->codeAuthSeed)
        + " timeout=" + std::to_string(config->msecTimeout)
        + " sizeofConfig=" + std::to_string(sizeof(AITalk_TConfig))
        + " sizeofTtsParam=" + std::to_string(sizeof(AITalk_TTtsParam))
        + " sizeofJobParam=" + std::to_string(sizeof(AITalk_TJobParam))
        + " offsetSpeakerVolume=" + std::to_string(offsetof(AITalk_TTtsParam, Speaker[0].volume)));
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_End(void)
{
    logLine("end");
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_LangClear(void)
{
    logLine("langclear");
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_LangLoad(const char *path)
{
    logLine("langload " + ptrStr(path));
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_VoiceLoad(const char *voiceName)
{
    g_voiceName = ptrStr(voiceName);
    logLine("voiceload " + ptrStr(voiceName));
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_VoiceClear(void)
{
    logLine("voiceclear");
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_ReloadPhraseDic(const char *path)
{
    logLine("reloadphrase " + ptrStr(path));
    return fileExists(path) ? AITALKERR_SUCCESS : AITALKERR_FILE_NOT_FOUND;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_ReloadWordDic(const char *path)
{
    logLine("reloadword " + ptrStr(path));
    return fileExists(path) ? AITALKERR_SUCCESS : AITALKERR_FILE_NOT_FOUND;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_ReloadSymbolDic(const char *path)
{
    logLine("reloadsymbol " + ptrStr(path));
    return fileExists(path) ? AITALKERR_SUCCESS : AITALKERR_FILE_NOT_FOUND;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_GetParam(AITalk_TTtsParam *param, uint32_t *size)
{
    if (param == nullptr)
    {
        if (size)
            *size = (uint32_t)sizeof(AITalk_TTtsParam);
        return AITALKERR_INSUFFICIENT;
    }
    // 実機 SDK と同様、param->size フィールドをバッファ容量として扱う
    // (0 のまま呼ぶと AITALKERR_INSUFFICIENT を返す)
    if (param->size < (uint32_t)sizeof(AITalk_TTtsParam))
        return AITALKERR_INSUFFICIENT;
    if (!g_hasParam)
    {
        memset(&g_param, 0, sizeof(g_param));
        g_param.size = (uint32_t)sizeof(AITalk_TTtsParam);
        g_param.volume = 1.0f;
        g_param.pauseBegin = 100;
        g_param.pauseTerm = 200;
        strncpy(g_param.voiceName, g_voiceName.c_str(), AITalk_TTtsParam::MAX_VOICENAME - 1);
        g_param.numSpeakers = 1;
        g_param.Speaker[0].volume = 1.0f;
        g_param.Speaker[0].speed = 1.0f;
        g_param.Speaker[0].pitch = 0.0f;
        g_param.Speaker[0].range = 1.0f;
        g_param.Speaker[0].pauseMiddle = 0;
        g_param.Speaker[0].pauseLong = 0;
        g_param.Speaker[0].pauseSentence = 300;
        g_hasParam = true;
    }
    uint32_t copySize = (*size < (uint32_t)sizeof(AITalk_TTtsParam)) ? *size : (uint32_t)sizeof(AITalk_TTtsParam);
    memcpy(param, &g_param, copySize);
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_SetParam(const AITalk_TTtsParam *param)
{
    memcpy(&g_param, param, sizeof(AITalk_TTtsParam));
    g_hasParam = true;
    logLine("setparam voice=" + ptrStr(param->Speaker[0].voiceName)
        + " volume=" + fmtFloat(param->Speaker[0].volume)
        + " speed=" + fmtFloat(param->Speaker[0].speed)
        + " pitch=" + fmtFloat(param->Speaker[0].pitch)
        + " range=" + fmtFloat(param->Speaker[0].range)
        + " pauseMiddle=" + std::to_string(param->Speaker[0].pauseMiddle)
        + " pauseLong=" + std::to_string(param->Speaker[0].pauseLong)
        + " pauseSentence=" + std::to_string(param->Speaker[0].pauseSentence)
        + " callbacks=" + std::to_string(param->procTextBuf ? 1 : 0)
        + std::to_string(param->procRawBuf ? 1 : 0)
        + std::to_string(param->procEventTts ? 1 : 0)
        + " extendFormat=" + std::to_string((int)param->extendFormat));
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_TextToKana(int32_t *jobId, AITalk_TJobParam *jobParam, const char *text)
{
    logLine("texttokana mode=" + std::to_string((int)jobParam->modeInOut) + " text=" + ptrStr(text));
    g_job = JobData();
    g_job.jobId = ++g_jobCounter;
    g_job.kana = std::string("[H]") + (text ? text : "") + std::string("[K]");
    *jobId = g_job.jobId;
    std::thread worker(textBufThread, g_param.procTextBuf, g_job.jobId);
    worker.detach();
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_GetKana(int32_t jobId, char *buffer, uint32_t bufferSize, uint32_t *readBytes, uint32_t *pos)
{
    if (g_job.kanaConsumed)
        return AITALKERR_NOMORE_DATA;
    size_t length = g_job.kana.size();
    if (length > bufferSize - 1)
        length = bufferSize - 1;
    memcpy(buffer, g_job.kana.data(), length);
    buffer[length] = 0;
    if (readBytes)
        *readBytes = (uint32_t)length;
    if (pos)
        *pos = 0;
    if (length >= g_job.kana.size())
        g_job.kanaConsumed = true;
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_CloseKana(int32_t jobId, int32_t option)
{
    logLine("closekana job=" + std::to_string(jobId));
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_TextToSpeech(int32_t *jobId, AITalk_TJobParam *jobParam, const char *kana)
{
    logLine("texttospeech mode=" + std::to_string((int)jobParam->modeInOut) + " kana=" + ptrStr(kana));
    g_job = JobData();
    g_job.jobId = ++g_jobCounter;
    // 4410 samples (0.1 s at 44100 Hz) of a ramp wave
    g_job.samples.resize(4410);
    for (int i = 0; i < 4410; i++)
        g_job.samples[i] = (short)((i * 7) % 20000 - 10000);
    *jobId = g_job.jobId;
    std::thread worker(rawBufThread, g_param.procRawBuf, g_job.jobId);
    worker.detach();
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_GetData(int32_t jobId, int16_t *buffer, uint32_t bufferSize, uint32_t *readSamples)
{
    if (g_job.samplesConsumed)
        return AITALKERR_NOMORE_DATA;
    size_t count = g_job.samples.size();
    if (count > bufferSize)
        count = bufferSize;
    memcpy(buffer, g_job.samples.data(), count * sizeof(int16_t));
    if (readSamples)
        *readSamples = (uint32_t)count;
    if (count >= g_job.samples.size())
        g_job.samplesConsumed = true;
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_CloseSpeech(int32_t jobId, int32_t option)
{
    logLine("closespeech job=" + std::to_string(jobId));
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_GetStatus(int32_t jobId, AITalkStatusCode *status)
{
    if (status)
        *status = AITALKSTAT_DONE;
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_GetJeitaControl(int32_t jobId, char *control)
{
    logLine("getjeitacontrol job=" + std::to_string(jobId));
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_LicenseDate(char *date)
{
    if (date)
        strcpy(date, "2026-01-01");
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_LicenseInfo(const char *name, char *info, uint32_t infoSize, uint32_t *size)
{
    logLine("licenseinfo " + ptrStr(name));
    if (size)
        *size = 2;
    if (info && infoSize >= 2)
    {
        info[0] = 'o';
        info[1] = 'k';
    }
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_ModuleFlag(void)
{
    logLine("moduleflag");
    return AITALKERR_SUCCESS;
}

__declspec(dllexport) AITalkResultCode AITalkAPI_VersionInfo(int32_t moduleId, char *version, uint32_t versionSize, uint32_t *size)
{
    if (size)
        *size = 8;
    if (version && versionSize >= 8)
        strcpy(version, "2.0.5.0");
    return AITALKERR_SUCCESS;
}

} // extern "C"
