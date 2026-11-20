#include "processor.h"
#include <algorithm>
#include <cmath>

ToneSettings tone;
constexpr double frequencies[]={80,250,1000,4000,10000};
void Processor::prepare(unsigned sampleRate,unsigned count) {
    rate=sampleRate; channels=count; cursor=0;
    capacity=rate/2+2;
    history.assign(size_t(capacity)*channels,0);
    memory.assign(count,{});
    current.fill(0); filters.fill({}); gainDb=0;
    delay=tone.delayMs.load()*int(rate)/1000;
    tone.sampleRate.store(rate);
}
void Processor::render(float* samples,unsigned frames) {
    if(!frames || !capacity) return;
    const bool enabled=tone.enabled.load();
    const double mix=1-std::exp(-double(frames)/(.025*rate));
    bool filtering=false;
    for(unsigned i=0;i<5;i++){
        const double wanted=enabled?tone.bands[i].load():0;
        double db=current[i]+mix*(wanted-current[i]);
        if(std::abs(db-wanted)<.005) db=wanted;
        if(db!=current[i]){
            const double w=6.283185307179586*std::min(frequencies[i],rate*.44)/rate;
            const double a=std::pow(10.,db/40), alpha=std::sin(w)/(2*.85);
            const double a0=1+alpha/a;
            filters[i]={(1+alpha*a)/a0,-2*std::cos(w)/a0,(1-alpha*a)/a0,-2*std::cos(w)/a0,(1-alpha/a)/a0};
            if(db==0) for(auto& channel:memory) channel[i]={};
            current[i]=db;
        }
        filtering |= db!=0;
    }
    const double target=enabled?tone.preamp.load():0;
    gainDb+=(target-gainDb)*mix;
    if(std::abs(target-gainDb)<.005) gainDb=target;
    const double gain=std::pow(10.,gainDb/20);
    int nextDelay=tone.delayMs.load()*int(rate)/1000;
    float peak=0;
    for(unsigned f=0;f<frames;f++){
        unsigned read=(cursor+capacity-delay)%capacity, next=(cursor+capacity-nextDelay)%capacity;
        float blend=float(f+1)/frames;
        for(unsigned c=0;c<channels;c++){
            double v=samples[size_t(f)*channels+c]*gain;
            if(filtering) for(unsigned i=0;i<5;i++){
                if(current[i]==0)continue;
                const auto& k=filters[i]; auto& m=memory[c][i];
                double y=k.b0*v+m.z1;
                m.z1=k.b1*v-k.a1*y+m.z2; m.z2=k.b2*v-k.a2*y; v=y;
            }
            history[size_t(cursor)*channels+c]=float(std::clamp(v,-1.,1.));
            float out=history[size_t(read)*channels+c];
            if(nextDelay!=delay) out+=(history[size_t(next)*channels+c]-out)*blend;
            samples[size_t(f)*channels+c]=out; peak=std::max(peak,std::abs(out));
        }
        cursor=(cursor+1)%capacity;
    }
    delay=nextDelay;
    float previous=tone.peak.load();
    while(previous<peak&&!tone.peak.compare_exchange_weak(previous,peak)){}
}
