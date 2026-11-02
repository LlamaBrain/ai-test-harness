#include "analysis.hpp"
#include <algorithm>
#include <cmath>
#include <set>
#include <tuple>
#include <optional>
namespace ath::visual {
namespace {
double linear(unsigned char channel) { double s=channel/255.0; return s<=0.04045?s/12.92:std::pow((s+0.055)/1.055,2.4); }
double luminance(cv::Vec3b c) { return .2126*linear(c[2])+.7152*linear(c[1])+.0722*linear(c[0]); }
double ratio(double a,double b) { return (std::max(a,b)+.05)/(std::min(a,b)+.05); }
double median(std::vector<double> values) { std::sort(values.begin(),values.end()); return values[values.size()/2]; }
Json bounds(cv::Rect r) { return {{"x",r.x},{"y",r.y},{"width",r.width},{"height",r.height}}; }
std::optional<double> measureContrast(const cv::Mat& image,cv::Rect r,double bg) {
    cv::Mat mask(r.height,r.width,CV_8U,cv::Scalar(0));
    for(int y=0;y<r.height;++y)for(int x=0;x<r.width;++x)
        if(ratio(luminance(image.at<cv::Vec3b>(r.y+y,r.x+x)),bg)>=1.1) mask.at<unsigned char>(y,x)=255;
    if(cv::countNonZero(mask)>.65*r.area()) return std::nullopt;
    cv::Mat labels,stats,centroids,core;
    int count=cv::connectedComponentsWithStats(mask,labels,stats,centroids,8);cv::erode(mask,core,cv::Mat());
    std::vector<std::vector<double>> values(count),interior(count);
    for(int y=0;y<r.height;++y)for(int x=0;x<r.width;++x) if(mask.at<unsigned char>(y,x)) {
        int label=labels.at<int>(y,x);double value=luminance(image.at<cv::Vec3b>(r.y+y,r.x+x));
        values[label].push_back(value);if(core.at<unsigned char>(y,x))interior[label].push_back(value);
    }
    double minimum=21;int glyphs=0;
    for(int i=1;i<count;++i) if(values[i].size()>=3) {
        ++glyphs;
        if(interior[i].empty()) {
            std::sort(values[i].begin(),values[i].end());
            if(values[i][values[i].size()*9/10]-values[i][values[i].size()/10]>.02) return std::nullopt;
            interior[i]=values[i];
        }
        minimum=std::min(minimum,ratio(median(interior[i]),bg));
    }
    return glyphs>=2?std::optional<double>(minimum):std::nullopt;
}
}
Json measure(const cv::Mat& image,const Region& region,const Policy& policy) {
    if(image.empty()||image.type()!=CV_8UC3) throw std::runtime_error("Expected decoded 8-bit RGB screenshot");
    if(policy.id.empty()||policy.version.empty()||!std::isfinite(policy.minHeightPx)||policy.minHeightPx<=0||!std::isfinite(policy.minContrastRatio)||policy.minContrastRatio<1||policy.minContrastRatio>21) throw std::runtime_error("Invalid policy identity or thresholds");
    const auto r=region.bounds;
    Json result={{"elementId",region.id},{"bounds",bounds(r)},{"detectionConfidence",region.detectionConfidence},
        {"measurementConfidence",0.0},{"measuredHeightPx",nullptr},{"contrastRatio",nullptr},
        {"heightOutcome","inconclusive"},{"contrastOutcome","inconclusive"},{"clippingOutcome","inconclusive"},
        {"message","Insufficient foreground/background evidence"}};
    if(r.width<3||r.height<3||r.x<0||r.y<0||r.x>image.cols-r.width||r.y>image.rows-r.height) {
        result["clippingOutcome"]="fail"; result["message"]="Expected bounds exceed viewport"; return result;
    }
    std::vector<double> edge;
    for(int y=r.y;y<r.y+r.height;++y) for(int x=r.x;x<r.x+r.width;++x)
        if(y==r.y||y==r.y+r.height-1||x==r.x||x==r.x+r.width-1) edge.push_back(luminance(image.at<cv::Vec3b>(y,x)));
    double bg=median(edge);
    // A border with materially different luminances cannot establish a local background.
    if(std::count_if(edge.begin(),edge.end(),[bg](double v){return std::abs(v-bg)>.025;})>edge.size()/20) {
        result["message"]="Complex background: border luminance is not stable"; return result;
    }
    auto independentContrast=measureContrast(image,r,bg);
    if(independentContrast) {
        result["contrastRatio"]=*independentContrast;
        result["contrastOutcome"]=*independentContrast>=policy.minContrastRatio?"pass":"fail";
    }
    cv::Mat mask(r.height,r.width,CV_8U,cv::Scalar(0));
    std::vector<double> foreground;
    for(int y=0;y<r.height;++y) for(int x=0;x<r.width;++x) {
        double value=luminance(image.at<cv::Vec3b>(r.y+y,r.x+x));
        if(ratio(value,bg)>=2.0) { mask.at<unsigned char>(y,x)=255; foreground.push_back(value); }
    }
    if(foreground.empty()) return result;
    cv::Mat labels,stats,centroids;
    int count=cv::connectedComponentsWithStats(mask,labels,stats,centroids,8);
    int top=r.height,bottom=-1,components=0,area=0;
    for(int i=1;i<count;++i) {
        int pixels=stats.at<int>(i,cv::CC_STAT_AREA);
        if(pixels<3) continue;
        ++components; area+=pixels;
        top=std::min(top,stats.at<int>(i,cv::CC_STAT_TOP));
        bottom=std::max(bottom,stats.at<int>(i,cv::CC_STAT_TOP)+stats.at<int>(i,cv::CC_STAT_HEIGHT)-1);
    }
    if(components<2||area>r.area()*.65||bottom<top) {result["message"]="Mask lacks sufficient glyph-like components";return result;}
    int tallest=0;
    for(int i=1;i<count;++i) if(stats.at<int>(i,cv::CC_STAT_AREA)>=3) tallest=std::max(tallest,stats.at<int>(i,cv::CC_STAT_HEIGHT));
    int commonTop=0,commonBottom=r.height;bool sizeAmbiguous=false;
    for(int i=1;i<count;++i) {
        int h=stats.at<int>(i,cv::CC_STAT_HEIGHT);
        if(stats.at<int>(i,cv::CC_STAT_AREA)<3||h<=3) continue;
        bool accent=false;
        for(int j=1;j<count&&h<tallest*.35;++j) {
            if(i==j||stats.at<int>(j,cv::CC_STAT_HEIGHT)<=h*2) continue;
            int gap=stats.at<int>(j,cv::CC_STAT_TOP)-stats.at<int>(i,cv::CC_STAT_TOP)-h;
            int left=std::max(stats.at<int>(i,cv::CC_STAT_LEFT),stats.at<int>(j,cv::CC_STAT_LEFT));
            int right=std::min(stats.at<int>(i,cv::CC_STAT_LEFT)+stats.at<int>(i,cv::CC_STAT_WIDTH),stats.at<int>(j,cv::CC_STAT_LEFT)+stats.at<int>(j,cv::CC_STAT_WIDTH));
            if(gap>=0&&gap<tallest*.35&&right>left) accent=true;
        }
        if(accent) continue;
        if(h<policy.minHeightPx&&tallest>=policy.minHeightPx) sizeAmbiguous=true;
        commonTop=std::max(commonTop,stats.at<int>(i,cv::CC_STAT_TOP));
        commonBottom=std::min(commonBottom,stats.at<int>(i,cv::CC_STAT_TOP)+h-1);
    }
    if(commonTop>commonBottom) {result["message"]="Multiple text lines cannot share a height measurement";return result;}
    const int height=bottom-top+1;
    const bool boundary=top==0||bottom==r.height-1||cv::countNonZero(mask.col(0))||cv::countNonZero(mask.col(r.width-1));
    result["measuredHeightPx"]=height;
    result["contrastRatio"]=independentContrast?Json(*independentContrast):Json(nullptr);
    result["measurementConfidence"]=boundary?.25:.8;
    result["clippingOutcome"]=boundary?"inconclusive":"pass";
    result["heightOutcome"]=boundary||sizeAmbiguous?"inconclusive":height>=policy.minHeightPx?"pass":region.representativeGlyphs?"fail":"inconclusive";
    result["contrastOutcome"]=boundary||!independentContrast?"inconclusive":*independentContrast>=policy.minContrastRatio?"pass":"fail";
    result["message"]=boundary?"Foreground touches region boundary":region.representativeGlyphs?"Visible pixel extent measured with 2:1 boundary inclusion":"Visible extent measured; absent representative glyphs cannot establish an undersize failure";
    return result;
}
AnalysisSession::AnalysisSession(const std::string& model) {
    if(model.empty()) return;
    detector_=std::make_unique<cv::dnn::TextDetectionModel_DB>(model);
    detector_->setPreferableBackend(cv::dnn::DNN_BACKEND_OPENCV);
    detector_->setPreferableTarget(cv::dnn::DNN_TARGET_CPU);
    detector_->setInputParams(1.0,cv::Size(736,736));
    detector_->setBinaryThreshold(.3).setPolygonThreshold(.5).setUnclipRatio(2.0);
}
Json analyze(const cv::Mat& image,const Json& manifest,const Policy& policy,const std::string& model) {
    return AnalysisSession(model).analyze(image,manifest,policy);
}
Json AnalysisSession::analyze(const cv::Mat& image,const Json& manifest,const Policy& policy) {
    if(image.empty()||image.type()!=CV_8UC3) throw std::runtime_error("Expected decoded 8-bit RGB screenshot");
    if(policy.id.empty()||policy.id.size()>256||policy.version.empty()||policy.version.size()>128||!std::isfinite(policy.minHeightPx)||policy.minHeightPx<=0||!std::isfinite(policy.minContrastRatio)||policy.minContrastRatio<1||policy.minContrastRatio>21) throw std::runtime_error("Invalid policy identity or thresholds");
    std::vector<Region> regions;
    bool strict=!manifest.is_null();
    if(strict) {
        for(const auto* key:{"screenId","screenshotId","locale"}) if(!manifest.contains(key)||!manifest.at(key).is_string()||manifest.at(key).get<std::string>().empty()||manifest.at(key).get<std::string>().size()>256) throw std::runtime_error(std::string("Manifest requires bounded nonempty ")+key);
        if(!manifest.contains("uiScale")||!manifest.at("uiScale").is_number()||!std::isfinite(manifest.at("uiScale").get<double>())||manifest.at("uiScale").get<double>()<=0) throw std::runtime_error("Manifest requires finite positive uiScale");
        if(!manifest.at("resolution").at("width").is_number_integer()||!manifest.at("resolution").at("height").is_number_integer()) throw std::runtime_error("Manifest resolution must use integer pixels");
        if(manifest.at("schemaVersion")!=1||manifest.at("resolution").at("width")!=image.cols||manifest.at("resolution").at("height")!=image.rows)
            throw std::runtime_error("Manifest schema or resolution mismatch");
        std::set<std::string> ids;
        if(!manifest.at("elements").is_array()||manifest.at("elements").size()>1024) throw std::runtime_error("Manifest elements must be an array of at most 1024 entries");
        for(const auto& e:manifest.at("elements")) {
            auto id=e.at("id").get<std::string>(); const auto& b=e.at("bounds");
            if(id.empty()||!ids.insert(id).second) throw std::runtime_error("Empty or duplicate element id");
            for(const auto* key:{"x","y","width","height"}) if(!b.at(key).is_number_integer()||(b.at(key).is_number_unsigned()&&b.at(key).get<uint64_t>()>16384)||b.at(key).get<int64_t>() < -16384||b.at(key).get<int64_t>()>16384) throw std::runtime_error("Bounds must contain bounded integer coordinates");
            if(b.at("width").get<int>()<=0||b.at("height").get<int>()<=0) throw std::runtime_error("Bounds must have positive area");
            regions.push_back({id,{b.at("x").get<int>(),b.at("y").get<int>(),b.at("width").get<int>(),b.at("height").get<int>()},e.value("representativeGlyphs",false),0.0});
            regions.back().expectedBounds=regions.back().bounds;
        }
    }
    if(detector_) {
        // PP-OCRv3 normalization from OpenCV Zoo's ppocr_det.py. Normalize
        // channels explicitly for compatibility with OpenCV's scalar-scale API.
        std::vector<std::vector<cv::Point>> polygons; std::vector<float> confidence;
        cv::Mat normalized; image.convertTo(normalized,CV_32FC3);
        std::vector<cv::Mat> channels;cv::split(normalized,channels);
        const double mean[]={123.675,116.28,103.53}; const double deviation[]={.229,.224,.225};
        for(int i=0;i<3;++i) channels[i]=(channels[i]-mean[i])/(255.0*deviation[i]);
        cv::merge(channels,normalized);
        detector_->detect(normalized,polygons,confidence);
        std::vector<int> matches(regions.size(),0);
        std::vector<bool> ambiguous(regions.size(),false);
        std::vector<cv::Rect> measuredBounds(regions.size());
        for(size_t i=0;i<polygons.size();++i) {
            auto detected=cv::boundingRect(polygons[i]);
            if(strict) {
                std::vector<size_t> candidates;
                for(size_t j=0;j<regions.size();++j) {
                    auto& region=regions[j];
                    // Detector evidence must predominantly lie in the expected region.
                    auto overlap=region.bounds&detected;
                    if(detected.area()>0&&double(overlap.area())/detected.area()>=.8) candidates.push_back(j);
                }
                if(candidates.size()==1) {
                    auto j=candidates[0];++matches[j];
                    if(matches[j]==1) {regions[j].detectionConfidence=confidence[i];measuredBounds[j]=detected;}
                    else {
                        if(std::max(measuredBounds[j].y,detected.y)>=std::min(measuredBounds[j].y+measuredBounds[j].height,detected.y+detected.height)) ambiguous[j]=true;
                        regions[j].detectionConfidence=std::min(regions[j].detectionConfidence,double(confidence[i]));
                        measuredBounds[j]|=detected;
                    }
                } else for(auto j:candidates) ambiguous[j]=true;
            } else regions.push_back({"",detected,false,confidence[i]});
        }
        if(strict) for(size_t j=0;j<regions.size();++j) {
            if(matches[j]>0&&!ambiguous[j]) {
                int padding=std::max(5,measuredBounds[j].height/3);
                regions[j].bounds=(measuredBounds[j]+cv::Size(padding*2,padding*2)-cv::Point(padding,padding))&regions[j].bounds;
            }
            else regions[j].detectionConfidence=0;
        }
    }
    std::sort(regions.begin(),regions.end(),[](const auto&a,const auto&b){return std::tie(a.bounds.y,a.bounds.x,a.id)<std::tie(b.bounds.y,b.bounds.x,b.id);});
    Json findings=Json::array(); bool failed=false,uncertain=regions.empty(); int measured=0;
    if(strict) for(const auto& e:manifest.at("elements")) {
        if(e.contains("expectedText")&&!e.at("expectedText").get<std::string>().empty()) uncertain=true;
    }
    for(size_t i=0;i<regions.size();++i) {
        if(regions[i].id.empty()) regions[i].id="detected-"+std::to_string(i);
        auto m=measure(image,regions[i],policy);
        if(strict) {
            auto expected=regions[i].expectedBounds;m["expectedBounds"]=bounds(expected);
            if(expected.x<0||expected.y<0||expected.x>image.cols-expected.width||expected.y>image.rows-expected.height) m["clippingOutcome"]="fail";
        }
        if(regions[i].detectionConfidence<=0) {
            uncertain=true;
            m["message"]="Text detection did not corroborate expected region; "+m["message"].get<std::string>();
            for(const auto* key:{"heightOutcome","contrastOutcome","clippingOutcome"})
                if(m[key]=="pass") m[key]="inconclusive";
        }
        if(!m["measuredHeightPx"].is_null()) ++measured;
        for(auto pair : {std::pair{"text.height","heightOutcome"},std::pair{"text.contrast","contrastOutcome"},std::pair{"text.clipping","clippingOutcome"}}) {
            auto f=m; f["ruleId"]=pair.first; f["outcome"]=m[pair.second];
            failed|=f["outcome"]=="fail"; uncertain|=f["outcome"]=="inconclusive";
            f.erase("heightOutcome");f.erase("contrastOutcome");f.erase("clippingOutcome"); findings.push_back(f);
        }
    }
    Json context=Json::object();
    if(strict) for(const auto* key:{"screenId","screenshotId","locale","uiScale"}) if(manifest.contains(key)) context[key]=manifest[key];
    return {{"schemaVersion",1},{"outcome",failed?"fail":uncertain?"inconclusive":"pass"},{"context",context},
        {"image",{{"width",image.cols},{"height",image.rows}}},{"policy",{{"id",policy.id},{"version",policy.version},{"minHeightPx",policy.minHeightPx},{"minContrastRatio",policy.minContrastRatio}}},
        {"findings",findings},{"coverage",{{"mode",strict?"manifest":"best-effort"},{"expected",strict?regions.size():0},{"measured",measured},
        {"limitations",Json::array({"Screenshot analysis is not accessibility certification","Uniform local backgrounds and isolated single-line regions required","Expected text content is not recognized or verified","Best-effort detection does not establish complete UI coverage"})}}}};
}
}
