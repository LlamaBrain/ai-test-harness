#include "analysis.hpp"
#include <chrono>
#include <iostream>
#ifdef _WIN32
#include <windows.h>
#include <psapi.h>
#else
#include <sys/resource.h>
#endif
using namespace ath::visual;
uint64_t peakMemory() {
#ifdef _WIN32
    PROCESS_MEMORY_COUNTERS info{}; GetProcessMemoryInfo(GetCurrentProcess(),&info,sizeof(info)); return info.PeakWorkingSetSize;
#else
    rusage info{};getrusage(RUSAGE_SELF,&info);
#ifdef __APPLE__
    return info.ru_maxrss;
#else
    return uint64_t(info.ru_maxrss)*1024;
#endif
#endif
}
int main(int argc,char** argv) {
    const auto started=std::chrono::steady_clock::now();
    AnalysisSession session(argc>1?argv[1]:"");
    auto loadMs=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-started).count();
    Json runs=Json::array();
    for(int height:{1080,1440,2160}) {
        cv::Mat image(height,height*16/9,CV_8UC3,cv::Scalar(0,0,0));
        cv::putText(image,"Agjp 123",{40,100},cv::FONT_HERSHEY_SIMPLEX,2,cv::Scalar(255,255,255),2);
        Json manifest={{"schemaVersion",1},{"resolution",{{"width",image.cols},{"height",height}}},{"elements",Json::array({{{"id","sample"},{"representativeGlyphs",true},{"bounds",{{"x",20},{"y",20},{"width",400},{"height",120}}}}})}};
        manifest["screenId"]="benchmark";manifest["screenshotId"]="in-memory";manifest["locale"]="en-US";manifest["uiScale"]=1.0;
        Policy policy{"benchmark","1",26,4.5};
        const auto begin=std::chrono::steady_clock::now();
        auto result=session.analyze(image,manifest,policy);
        auto firstMs=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-begin).count();
        const auto warm=std::chrono::steady_clock::now();
        for(int i=0;i<5;++i) session.analyze(image,manifest,policy);
        auto elapsed=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-warm).count();
        runs.push_back({{"width",image.cols},{"height",height},{"iterations",5},{"firstAnalysisMs",firstMs},{"meanAnalysisMs",elapsed/5},{"imagesPerSecond",5000/elapsed},{"outcome",result["outcome"]},{"peakProcessMemoryBytes",peakMemory()}});
    }
    std::cout<<Json{{"schemaVersion",1},{"scope","In-process CPU batch reuses model; excludes PNG decode and OS process startup"},{"modelLoaded",argc>1},{"modelLoadMs",loadMs},{"runs",runs}}.dump(2)<<'\n';
}
