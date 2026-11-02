#include "analysis.hpp"
#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <cmath>
#include <fcntl.h>
#ifdef _WIN32
#include <io.h>
#include <sys/stat.h>
#else
#include <unistd.h>
#endif
using namespace ath::visual;
namespace fs=std::filesystem;
namespace {
Json readJson(const std::string& path) { if(fs::file_size(path)>1024*1024) throw std::runtime_error("JSON input exceeds 1 MiB"); std::ifstream stream(path); return Json::parse(stream); }
void exclusive(const std::string& path,const unsigned char* data,size_t size) {
#ifdef _WIN32
    int fd=_open(path.c_str(),_O_WRONLY|_O_CREAT|_O_EXCL|_O_BINARY,_S_IREAD|_S_IWRITE);
#else
    int fd=open(path.c_str(),O_WRONLY|O_CREAT|O_EXCL,0600);
#endif
    if(fd<0) throw std::runtime_error("Cannot exclusively create output: "+path);
    size_t offset=0;
    while(offset<size) {
#ifdef _WIN32
        int written=_write(fd,data+offset,static_cast<unsigned int>(std::min(size-offset,size_t(1048576))));
#else
        auto written=write(fd,data+offset,size-offset);
#endif
        if(written<=0) {
#ifdef _WIN32
            _close(fd);
#else
            close(fd);
#endif
            throw std::runtime_error("Cannot write output: "+path);
        }
        offset+=written;
    }
#ifdef _WIN32
    _close(fd);
#else
    close(fd);
#endif
}
}
Json execute(const std::map<std::string,std::string>& args,AnalysisSession& session) {
        if(!args.contains("--input")) throw std::runtime_error("Required: --input screenshot.png [--manifest file] [--policy file] [--model ppocr.onnx] [--annotated file]");
        auto size=fs::file_size(args.at("--input"));
        if(size<33||size>32*1024*1024) throw std::runtime_error("PNG must be 33 bytes to 32 MiB");
        std::ifstream input(args.at("--input"),std::ios::binary); std::vector<unsigned char> bytes(size); input.read(reinterpret_cast<char*>(bytes.data()),size);
        const std::vector<unsigned char> signature{137,80,78,71,13,10,26,10};
        if(!input||!std::equal(signature.begin(),signature.end(),bytes.begin())) throw std::runtime_error("Input is not a complete PNG");
        auto u32=[&](size_t offset)->uint32_t{return (uint32_t(bytes.at(offset))<<24)|(uint32_t(bytes.at(offset+1))<<16)|(uint32_t(bytes.at(offset+2))<<8)|bytes.at(offset+3);};
        auto width=u32(16),height=u32(20);
        if(!width||!height||width>16384||height>16384||uint64_t(width)*height>33554432) throw std::runtime_error("PNG dimensions exceed analysis limits");
        bool ended=false;
        for(size_t offset=8;offset+12<=bytes.size();) {
            uint32_t length=u32(offset); if(length>bytes.size()-offset-12) throw std::runtime_error("Truncated PNG chunk");
            uint32_t crc=0xffffffffu;
            for(size_t j=offset+4;j<offset+8+length;++j) {
                crc^=bytes[j];
                for(int bit=0;bit<8;++bit) crc=(crc>>1)^((crc&1)?0xedb88320u:0u);
            }
            if((crc^0xffffffffu)!=u32(offset+8+length)) throw std::runtime_error("PNG chunk CRC mismatch");
            if(std::string(reinterpret_cast<const char*>(bytes.data()+offset+4),4)=="IEND") {ended=length==0&&offset+12==bytes.size(); break;}
            offset+=12+length;
        }
        if(!ended) throw std::runtime_error("PNG lacks final IEND chunk");
        auto image=cv::imdecode(bytes,cv::IMREAD_UNCHANGED);
        if(image.empty()||image.depth()!=CV_8U) throw std::runtime_error("PNG decode failed or unsupported bit depth");
        if(image.channels()==4) {
            std::vector<cv::Mat> channels;cv::split(image,channels);
            if(cv::countNonZero(channels[3]!=255)) throw std::runtime_error("Screenshot contains nonopaque pixels; composite before analysis");
            cv::cvtColor(image,image,cv::COLOR_BGRA2BGR);
        } else if(image.channels()==1) cv::cvtColor(image,image,cv::COLOR_GRAY2BGR);
        Policy policy{"custom-default","1",26,4.5};
        if(args.contains("--policy")) {
            auto p=readJson(args.at("--policy")); if(p.at("schemaVersion")!=1) throw std::runtime_error("Unsupported policy schema");
            for(const auto* key:{"id","version","source"}) {
                if(!p.contains(key)||!p.at(key).is_string()||p.at(key).get<std::string>().find_first_not_of(" \t\r\n")==std::string::npos)
                    throw std::runtime_error(std::string("Policy requires nonempty ")+key);
            }
            if(p.contains("scaling")||p.contains("referenceHeight")||p.contains("analyzedHeight")) {
                if(p.value("scaling",std::string())!="linear-height"||!p.contains("referenceHeight")||!p.at("referenceHeight").is_number_integer()||p.at("referenceHeight").get<double>()<=0||
                   !p.contains("analyzedHeight")||!p.at("analyzedHeight").is_number_integer()||p.at("analyzedHeight")!=image.rows)
                    throw std::runtime_error("Resolve this scaling profile for the screenshot height with ath-exe before native analysis; analyzedHeight must match the PNG");
            }
            policy={p.at("id"),p.at("version"),p.at("minHeightPx"),p.at("minContrastRatio")};
            policy.scope=p.value("scope",std::string("default-rendered-size"));
            policy.source=p.at("source").get<std::string>();
            if(!std::isfinite(policy.minHeightPx)||policy.minHeightPx<=0||!std::isfinite(policy.minContrastRatio)||policy.minContrastRatio<1||policy.minContrastRatio>21) throw std::runtime_error("Invalid policy thresholds");
        }
        auto result=session.analyze(image,args.contains("--manifest")?readJson(args.at("--manifest")):Json(),policy);
        if(args.contains("--annotated")) {
            auto annotation=image.clone();
            for(const auto& f:result["findings"]) if(f["ruleId"]=="text.height"&&f["bounds"].is_object()) {
                auto b=f["bounds"];cv::Rect r(b["x"],b["y"],b["width"],b["height"]);
                cv::rectangle(annotation,r,f["outcome"]=="pass"?cv::Scalar(0,200,0):cv::Scalar(0,0,255),2);
            }
            std::vector<unsigned char> encoded;if(!cv::imencode(".png",annotation,encoded)) throw std::runtime_error("Annotation encoding failed");
            exclusive(args.at("--annotated"),encoded.data(),encoded.size());
        }
        return result;
}
int main(int argc,char** argv) {
    try {
        std::map<std::string,std::string> args;
        for(int i=1;i<argc;++i) {
            std::string key=argv[i];
            if(key!="--input"&&key!="--output"&&key!="--manifest"&&key!="--policy"&&key!="--model"&&key!="--annotated"&&key!="--batch") throw std::runtime_error("Unknown option: "+key);
            if(++i>=argc||args.contains(key)) throw std::runtime_error("Missing value or duplicate option: "+key);
            args[key]=argv[i];
        }
        AnalysisSession session(args.contains("--model")?args.at("--model"):"");
        Json result;
        if(args.contains("--batch")) {
            if(args.contains("--input")||args.contains("--manifest")||args.contains("--annotated")||args.contains("--policy")) throw std::runtime_error("Batch items own input, manifest, policy and annotation options");
            auto batch=readJson(args.at("--batch"));
            if(batch.at("schemaVersion")!=1||!batch.at("items").is_array()||batch.at("items").empty()||batch.at("items").size()>1024) throw std::runtime_error("Invalid batch schema or item count");
            Json results=Json::array(); bool error=false,failed=false,uncertain=false;
            for(const auto& item:batch.at("items")) {
                Json entry;
                try {
                    std::map<std::string,std::string> itemArgs;
                    for(const auto* key:{"input","manifest","policy","annotated"}) if(item.contains(key)) itemArgs[std::string("--")+key]=item.at(key).get<std::string>();
                    entry=execute(itemArgs,session);
                } catch(const std::exception& e) {entry={{"schemaVersion",1},{"outcome","infrastructure-error"},{"error",e.what()}};}
                error|=entry["outcome"]=="infrastructure-error"; failed|=entry["outcome"]=="fail"; uncertain|=entry["outcome"]=="inconclusive";
                results.push_back(entry);
            }
            result={{"schemaVersion",1},{"outcome",error?"infrastructure-error":failed?"fail":uncertain?"inconclusive":"pass"},{"results",results}};
        } else result=execute(args,session);
        auto json=result.dump(2)+"\n";
        if(args.contains("--output")) exclusive(args.at("--output"),reinterpret_cast<const unsigned char*>(json.data()),json.size());
        std::cout<<json;
        return result["outcome"]=="pass"?0:result["outcome"]=="fail"?1:result["outcome"]=="inconclusive"?2:3;
    } catch(const std::exception& e) {std::cout<<Json{{"schemaVersion",1},{"outcome","infrastructure-error"},{"error",e.what()}}.dump()<<'\n';return 3;}
}
