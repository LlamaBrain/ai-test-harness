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
cv::Rect clearBorder(const cv::Mat& image,cv::Rect region,cv::Rect limit) {
    limit&=cv::Rect(0,0,image.cols,image.rows);region&=limit;
    if(region.empty()||limit.empty()) return region;
    double bg=luminance(image.at<cv::Vec3b>(limit.y,limit.x));
    for(int iteration=0;iteration<4096;++iteration) {
        bool left=false,right=false,top=false,bottom=false;
        for(int y=region.y;y<region.y+region.height;++y) {
            left|=ratio(luminance(image.at<cv::Vec3b>(y,region.x)),bg)>=1.1;
            right|=ratio(luminance(image.at<cv::Vec3b>(y,region.x+region.width-1)),bg)>=1.1;
        }
        for(int x=region.x;x<region.x+region.width;++x) {
            top|=ratio(luminance(image.at<cv::Vec3b>(region.y,x)),bg)>=1.1;
            bottom|=ratio(luminance(image.at<cv::Vec3b>(region.y+region.height-1,x)),bg)>=1.1;
        }
        auto expanded=cv::Rect(region.x-(left?4:0),region.y-(top?4:0),region.width+(left?4:0)+(right?4:0),region.height+(top?4:0)+(bottom?4:0))&limit;
        if(expanded==region) break;
        region=expanded;
    }
    return region;
}
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
        std::sort(interior[i].begin(),interior[i].end());
        if(interior[i][interior[i].size()*9/10]-interior[i][interior[i].size()/10]>.1) return std::nullopt;
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
    std::vector<int> glyphTop(count),glyphBottom(count);std::vector<bool> attached(count,false);
    for(int i=1;i<count;++i) {glyphTop[i]=stats.at<int>(i,cv::CC_STAT_TOP);glyphBottom[i]=glyphTop[i]+stats.at<int>(i,cv::CC_STAT_HEIGHT)-1;}
    for(int i=1;i<count;++i) {
        int h=stats.at<int>(i,cv::CC_STAT_HEIGHT);
        if(h>=tallest*.35) continue;
        int owner=0;
        for(int j=1;j<count;++j) {
            if(i==j||stats.at<int>(j,cv::CC_STAT_HEIGHT)<=h*2) continue;
            int gap=glyphTop[j]-glyphBottom[i]-1,lowerGap=glyphTop[i]-glyphBottom[j]-1;
            int left=std::max(stats.at<int>(i,cv::CC_STAT_LEFT),stats.at<int>(j,cv::CC_STAT_LEFT));
            int right=std::min(stats.at<int>(i,cv::CC_STAT_LEFT)+stats.at<int>(i,cv::CC_STAT_WIDTH),stats.at<int>(j,cv::CC_STAT_LEFT)+stats.at<int>(j,cv::CC_STAT_WIDTH));
            if(((gap>=0&&gap<tallest*.35)||(lowerGap>=0&&lowerGap<tallest*.35))&&right>left) {if(owner) {owner=-1;break;}owner=j;}
        }
        if(owner>0) {attached[i]=true;glyphTop[owner]=std::min(glyphTop[owner],glyphTop[i]);glyphBottom[owner]=std::max(glyphBottom[owner],glyphBottom[i]);}
    }
    tallest=0;for(int i=1;i<count;++i) if(!attached[i]) tallest=std::max(tallest,glyphBottom[i]-glyphTop[i]+1);
    int commonTop=0,commonBottom=r.height,minGlyphTop=r.height,maxGlyphTop=0,minGlyphBottom=r.height,maxGlyphBottom=0,shortest=tallest;
    for(int i=1;i<count;++i) {
        int h=glyphBottom[i]-glyphTop[i]+1;
        if(stats.at<int>(i,cv::CC_STAT_AREA)<3||h<=3) continue;
        if(attached[i]) continue;
        minGlyphTop=std::min(minGlyphTop,glyphTop[i]);maxGlyphTop=std::max(maxGlyphTop,glyphTop[i]);
        minGlyphBottom=std::min(minGlyphBottom,glyphBottom[i]);maxGlyphBottom=std::max(maxGlyphBottom,glyphBottom[i]);
        shortest=std::min(shortest,h);
        commonTop=std::max(commonTop,glyphTop[i]);
        commonBottom=std::min(commonBottom,glyphBottom[i]);
    }
    if(commonTop>commonBottom) {result["message"]="Multiple text lines cannot share a height measurement";return result;}
    // Joint upper/lower bands support ordinary ascenders and descenders. A
    // height split sharing only one baseline is unresolved mixed-size evidence.
    // The classification never depends on the selected policy threshold.
    int upperBand=maxGlyphTop-minGlyphTop,lowerBand=maxGlyphBottom-minGlyphBottom;
    // Round glyphs may overshoot flat glyphs by one raster pixel at each edge;
    // compare the bands independently rather than allowing only one total pixel.
    bool rasterOvershoot=upperBand<=1&&lowerBand<=1;
    bool sizeAmbiguous=!rasterOvershoot&&tallest-shortest>1&&(upperBand<=1||lowerBand<=1||std::max(upperBand,lowerBand)>2*std::min(upperBand,lowerBand));
    const int height=bottom-top+1;
    const bool boundary=top==0||bottom==r.height-1||cv::countNonZero(mask.col(0))||cv::countNonZero(mask.col(r.width-1));
    result["measuredHeightPx"]=height;
    result["contrastRatio"]=independentContrast?Json(*independentContrast):Json(nullptr);
    result["measurementConfidence"]=boundary?.25:.8;
    result["clippingOutcome"]=boundary?"inconclusive":"pass";
    result["heightOutcome"]=boundary?"inconclusive":height<policy.minHeightPx?(region.representativeGlyphs?"fail":"inconclusive"):sizeAmbiguous?"inconclusive":"pass";
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
    if(policy.scope!="default-rendered-size"&&policy.scope!="adjustable-size-capability") throw std::runtime_error("Unsupported visual policy scope");
    if(policy.source.find_first_not_of(" \t\r\n")==std::string::npos||policy.source.size()>4096) throw std::runtime_error("Policy requires a bounded nonempty source");
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
        size_t elementIndex=0;
        for(const auto& e:manifest.at("elements")) {
            if(!e.is_object()) throw std::runtime_error("Manifest element must be an object");
            auto id=e.contains("id")?e.at("id").get<std::string>():"expected-"+std::to_string(elementIndex);
            ++elementIndex;
            if(id.empty()||id.size()>256||!ids.insert(id).second) throw std::runtime_error("Empty, oversized or duplicate authored/generated element id");
            if(!e.contains("bounds")) {
                Region missing;missing.id=id;missing.hasBounds=false;regions.push_back(missing);continue;
            }
            const auto& b=e.at("bounds");
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
                    if(!region.hasBounds) continue;
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
            } else {
                auto padded=clearBorder(image,detected+cv::Size(10,10)-cv::Point(5,5),cv::Rect(0,0,image.cols,image.rows));
                regions.push_back({"",padded,false,confidence[i]});
            }
        }
        if(strict) for(size_t j=0;j<regions.size();++j) {
            if(matches[j]>0&&!ambiguous[j]) {
                int padding=std::max(5,measuredBounds[j].height/3);
                regions[j].bounds=(measuredBounds[j]+cv::Size(padding*2,padding*2)-cv::Point(padding,padding))&regions[j].bounds;
                regions[j].bounds=clearBorder(image,regions[j].bounds,regions[j].expectedBounds);
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
        Json m;
        if(regions[i].hasBounds) m=measure(image,regions[i],policy);
        else m={{"elementId",regions[i].id},{"bounds",nullptr},{"expectedBounds",nullptr},{"detectionConfidence",0.0},{"measurementConfidence",0.0},{"measuredHeightPx",nullptr},{"contrastRatio",nullptr},
            {"heightOutcome","inconclusive"},{"contrastOutcome","inconclusive"},{"clippingOutcome","inconclusive"},{"message","Expected region has no render bounds; screenshot correspondence cannot be established"}};
        if(strict&&regions[i].hasBounds) {
            auto expected=regions[i].expectedBounds;m["expectedBounds"]=bounds(expected);
            if(expected.x<0||expected.y<0||expected.x>image.cols-expected.width||expected.y>image.rows-expected.height) m["clippingOutcome"]="fail";
        }
        if(regions[i].hasBounds&&regions[i].detectionConfidence<=0) {
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
    Json limitations=Json::array({"Screenshot analysis is not accessibility certification","Uniform local backgrounds and isolated single-line regions required","Expected text content is not recognized or verified","Best-effort detection does not establish complete UI coverage"});
    if(policy.scope=="adjustable-size-capability") {
        const std::string message="A screenshot cannot establish that users can adjust text size; separate runtime capability evidence is required";
        findings.push_back({{"ruleId","text.adjustability"},{"elementId",""},{"outcome","inconclusive"},{"bounds",nullptr},{"measuredHeightPx",nullptr},{"contrastRatio",nullptr},{"detectionConfidence",nullptr},{"measurementConfidence",nullptr},{"message",message}});
        limitations.push_back(message);uncertain=true;
    }
    return {{"schemaVersion",1},{"outcome",failed?"fail":uncertain?"inconclusive":"pass"},{"context",context},
        {"image",{{"width",image.cols},{"height",image.rows}}},{"policy",{{"id",policy.id},{"version",policy.version},{"source",policy.source},{"minHeightPx",policy.minHeightPx},{"minContrastRatio",policy.minContrastRatio},{"scope",policy.scope}}},
        {"findings",findings},{"coverage",{{"mode",strict?"manifest":"best-effort"},{"expected",strict?regions.size():0},{"measured",measured},
        {"limitations",limitations}}}};
}
}
