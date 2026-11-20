#pragma once
#include <array>
#include <atomic>
#include <vector>

struct ToneSettings {
    std::array<std::atomic<float>,5> bands{};
    std::atomic<float> preamp{0}, peak{0};
    std::atomic<int> delayMs{0}, sampleRate{48000};
    std::atomic<bool> enabled{true};
};
extern ToneSettings tone;
class Processor {
    struct Filter { double b0=1,b1=0,b2=0,a1=0,a2=0; };
    struct Memory { double z1=0,z2=0; };
    std::array<Filter,5> filters{};
    std::array<double,5> current{};
    std::vector<std::array<Memory,5>> memory;
    std::vector<float> history;
    unsigned rate=48000, channels=2, cursor=0, capacity=0;
    double gainDb=0;
    int delay=0;
public:
    void prepare(unsigned sampleRate, unsigned count);
    void render(float* samples,unsigned frames);
};
