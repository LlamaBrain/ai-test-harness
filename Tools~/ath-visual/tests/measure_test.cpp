#include "analysis.hpp"
#include <iostream>
#include <stdexcept>
#include <fstream>
using namespace ath::visual;
void require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
int main() {
    try {
        Policy policy{"test", "1", 20, 4.5};
        for (bool dark : {false, true}) {
            for (int height : {19, 20, 21}) {
                cv::Mat image(80, 120, CV_8UC3, dark ? cv::Scalar(255,255,255) : cv::Scalar(0,0,0));
                // Independent hand-defined glyph masks: extent is exactly height, not an OCR box.
                for(int x : {20,35,50}) cv::rectangle(image, {x,20,6,height}, dark ? cv::Scalar(0,0,0) : cv::Scalar(255,255,255), cv::FILLED);
                auto result = measure(image, {"label", {10,10,70,50}, true, 1.0}, policy);
                require(result["measuredHeightPx"] == height, "pixel extent");
                require(result["heightOutcome"] == (height < 20 ? "fail" : "pass"), "threshold boundary");
                require(result["contrastOutcome"] == "pass", "contrast polarity");
                auto uncertain = measure(image, {"label", {10,10,70,50}, false, 1.0}, policy);
                if(height < 20) require(uncertain["heightOutcome"] == "inconclusive", "short label must not fail");
            }
        }
        cv::Mat empty(80,120,CV_8UC3,cv::Scalar(0,0,0));
        auto missing = measure(empty, {"missing",{10,10,70,50},true,1.0},policy);
        require(missing["heightOutcome"] == "inconclusive", "missing text cannot pass");
        require(analyze(empty,nullptr,policy,"")["outcome"]=="inconclusive", "empty detection cannot pass");
        Json manifest={{"schemaVersion",1},{"resolution",{{"width",120},{"height",80}}},{"elements",Json::array({{{"id","missing"},{"bounds",{{"x",10},{"y",10},{"width",70},{"height",50}}}}})}};
        manifest["screenId"]="fixture";manifest["screenshotId"]="fixture.png";manifest["locale"]="en-US";manifest["uiScale"]=1.0;
        require(analyze(empty,manifest,policy,"")["outcome"]=="inconclusive", "missing manifest text cannot pass");
        auto clipped=measure(empty,{"outside",{-1,10,70,50},true,1.0},policy);
        require(clipped["clippingOutcome"]=="fail", "out of viewport bounds");
        for(int resolution:{1080,1440,2160}) {
            cv::Mat screenshot(resolution,resolution*16/9,CV_8UC3,cv::Scalar(0,0,0));
            int h=resolution/54;
            // Independent hand-authored HpH raster: cap height h-5, descender to h.
            for(int x:{20,60}) {
                cv::rectangle(screenshot,{x,20,3,h-5},cv::Scalar(255,255,255),cv::FILLED);
                cv::rectangle(screenshot,{x+10,20,3,h-5},cv::Scalar(255,255,255),cv::FILLED);
                cv::rectangle(screenshot,{x,20+(h-5)/2,13,3},cv::Scalar(255,255,255),cv::FILLED);
            }
            cv::rectangle(screenshot,{40,25,3,h-5},cv::Scalar(255,255,255),cv::FILLED);
            cv::rectangle(screenshot,{40,25,13,h-10},cv::Scalar(255,255,255),2);
            auto result=measure(screenshot,{"HpH",{10,10,80,h+20},true,1.0},policy);
            require(result["measuredHeightPx"]==h,"resolution fixture extent");
            require(cv::imwrite("fixture-"+std::to_string(resolution)+".png",screenshot),"fixture PNG write");
        }
        cv::Mat complex(80,120,CV_8UC3);
        for(int y=0;y<80;++y)for(int x=0;x<120;++x)complex.at<cv::Vec3b>(y,x)=cv::Vec3b(x*2,x*2,x*2);
        require(measure(complex,{"gradient",{10,10,70,50},true,1.0},policy)["contrastOutcome"]=="inconclusive","gradient separation");
        int labeled=0,falsePositive=0,falseNegative=0;
        for(int font:{cv::FONT_HERSHEY_SIMPLEX,cv::FONT_HERSHEY_DUPLEX,cv::FONT_HERSHEY_COMPLEX,cv::FONT_HERSHEY_TRIPLEX})
        for(int weight:{1,2,3}) for(int raster:{cv::LINE_8,cv::LINE_AA}) {
            cv::Mat sample(160,600,CV_8UC3,cv::Scalar(0,0,0));
            cv::putText(sample,"Agjp Qy 0123",{20,90},font,1.8,cv::Scalar(255,255,255),weight,raster);
            // Independent ground truth for achromatic white-on-black: sRGB 64
            // is the first integer channel value with linear luminance >= .05.
            int top=sample.rows,bottom=-1;
            for(int y=0;y<sample.rows;++y) for(int x=0;x<sample.cols;++x)
                if(sample.at<cv::Vec3b>(y,x)[0]>=64) {top=std::min(top,y);bottom=std::max(bottom,y);}
            int truth=bottom-top+1;
            for(int delta:{-1,0,1}) {
                Policy labeledPolicy{"labeled","1",double(truth+delta),4.5};
                auto actual=measure(sample,{"representative",{5,5,580,145},true,1.0},labeledPolicy);
                if(actual["measuredHeightPx"]!=truth) throw std::runtime_error("Hershey pixel ground truth font="+std::to_string(font)+" weight="+std::to_string(weight)+" raster="+std::to_string(raster)+" truth="+std::to_string(truth)+" actual="+actual.dump());
                bool shouldFail=delta>0;bool failed=actual["heightOutcome"]=="fail";
                falsePositive+=failed&&!shouldFail;falseNegative+=!failed&&shouldFail;++labeled;
            }
            cv::imwrite("font-"+std::to_string(font)+"-"+std::to_string(weight)+"-"+std::to_string(raster)+".png",sample);
        }
        std::ofstream("fixture-metrics.json")<<Json{{"labeledChecks",labeled},{"falsePositives",falsePositive},{"falseNegatives",falseNegative},{"scope","Synthetic uniform-background Latin Hershey text height; not real-world detector accuracy"}}.dump(2);
        require(!falsePositive&&!falseNegative,"labeled height outcomes");
        cv::Mat multi(100,200,CV_8UC3,cv::Scalar(0,0,0));
        for(int y:{20,50}) for(int x:{20,40,60}) cv::rectangle(multi,{x,y,6,10},cv::Scalar(255,255,255),cv::FILLED);
        require(measure(multi,{"multiline",{5,5,180,90},true,1.0},Policy{"test","1",26,4.5})["heightOutcome"]!="pass","separated short lines cannot union-pass");
        cv::Mat mixed(100,200,CV_8UC3,cv::Scalar(0,0,0));
        for(int x:{20,40})cv::rectangle(mixed,{x,40,6,20},cv::Scalar(255,255,255),cv::FILLED);
        for(int x:{60,80})cv::rectangle(mixed,{x,20,6,40},cv::Scalar(255,255,255),cv::FILLED);
        require(measure(mixed,{"mixed",{5,5,180,90},true,1.0},Policy{"test","1",26,4.5})["heightOutcome"]!="pass","mixed sizes cannot union-pass");
        mixed.setTo(cv::Scalar(0,0,0));
        for(int x:{20,40})cv::rectangle(mixed,{x,35,6,25},cv::Scalar(255,255,255),cv::FILLED);
        for(int x:{60,80})cv::rectangle(mixed,{x,20,6,40},cv::Scalar(255,255,255),cv::FILLED);
        bool leftPass=false;
        for(int threshold=1;threshold<=60;++threshold) {
            auto outcome=measure(mixed,{"mixed25-40",{5,5,180,90},true,1.0},Policy{"test","1",double(threshold),4.5})["heightOutcome"];
            require(outcome!="pass","mixed25/40 cohort remains ambiguous");
            if(outcome!="pass")leftPass=true;
            require(!leftPass||outcome!="pass","tightening threshold cannot turn uncertainty into pass");
        }
        auto invalidManifest=manifest;
        invalidManifest["elements"][0]["bounds"]["x"]=uint64_t(-1);
        bool rejected=false;try {analyze(empty,invalidManifest,policy,"");}catch(const std::exception&){rejected=true;}
        require(rejected,"unsigned coordinate wrap rejected");
        invalidManifest=manifest;invalidManifest.erase("locale");rejected=false;
        try {analyze(empty,invalidManifest,policy,"");}catch(const std::exception&){rejected=true;}
        require(rejected,"missing required manifest context rejected");
        cv::Mat punctuation(120,160,CV_8UC3,cv::Scalar(0,0,0));
        cv::rectangle(punctuation,{20,34,6,56},cv::Scalar(255,255,255),cv::FILLED);
        cv::rectangle(punctuation,{40,34,6,41},cv::Scalar(255,255,255),cv::FILLED);
        cv::rectangle(punctuation,{40,82,6,8},cv::Scalar(255,255,255),cv::FILLED);
        cv::rectangle(punctuation,{60,33,6,42},cv::Scalar(255,255,255),cv::FILLED);
        cv::rectangle(punctuation,{60,82,6,8},cv::Scalar(255,255,255),cv::FILLED);
        for(int threshold:{56,57}) {
            auto p=measure(punctuation,{"punctuation",{5,5,140,110},false,1.0},Policy{"test","1",double(threshold),4.5});
            require(p["measuredHeightPx"]==57&&p["heightOutcome"]=="pass","detached punctuation counts as complete glyph with monotonic threshold results");
        }
        cv::Mat overshoot(90,160,CV_8UC3,cv::Scalar(0,0,0));
        cv::rectangle(overshoot,{20,20,6,39},cv::Scalar(255,255,255),cv::FILLED);
        cv::rectangle(overshoot,{40,21,6,37},cv::Scalar(255,255,255),cv::FILLED);
        cv::rectangle(overshoot,{60,20,6,38},cv::Scalar(255,255,255),cv::FILLED);
        for(int threshold:{38,39}) {
            auto p=measure(overshoot,{"round-flat-caps",{5,5,140,80},false,1.0},Policy{"test","1",double(threshold),4.5});
            require(p["measuredHeightPx"]==39&&p["heightOutcome"]=="pass","onepixel overshoot at each edge must preserve valid cap-height measurement");
        }
        cv::Mat contrastMix(100,200,CV_8UC3,cv::Scalar(0,0,0));
        for(int x:{20,40,60})cv::rectangle(contrastMix,{x,20,6,30},cv::Scalar(255,255,255),cv::FILLED);
        cv::rectangle(contrastMix,{80,20,6,30},cv::Scalar(90,90,90),cv::FILLED);
        require(measure(contrastMix,{"mixedContrast",{5,5,180,90},true,1.0},policy)["contrastOutcome"]=="fail","bright majority cannot hide low-contrast glyph");
        cv::rectangle(contrastMix,{80,20,6,30},cv::Scalar(50,50,50),cv::FILLED);
        require(measure(contrastMix,{"faint",{5,5,180,90},true,1.0},policy)["contrastOutcome"]!="pass","below-boundary glyph cannot silently disappear");
        cv::Mat low(100,200,CV_8UC3,cv::Scalar(0,0,0));
        for(int x:{20,40,60})cv::rectangle(low,{x,20,6,30},cv::Scalar(50,50,50),cv::FILLED);
        require(measure(low,{"lowContrast",{5,5,180,90},true,1.0},policy)["contrastOutcome"]=="fail","contrast below2:1 remains independently measurable");
        cv::Mat outlined(100,200,CV_8UC3,cv::Scalar(0,0,0));
        for(int x:{20,50,80}) {
            cv::rectangle(outlined,{x,20,12,30},cv::Scalar(255,255,255),cv::FILLED);
            cv::rectangle(outlined,{x+3,23,6,24},cv::Scalar(90,90,90),cv::FILLED);
        }
        require(measure(outlined,{"outline",{5,5,180,90},true,1.0},policy)["contrastOutcome"]=="inconclusive","multitone outlines need conservative contrast");
        cv::Mat texture(100,200,CV_8UC3);
        for(int y=0;y<texture.rows;++y)for(int x=0;x<texture.cols;++x) {
            auto shade=static_cast<unsigned char>(((x/5+y/5)%2)?20:100);
            texture.at<cv::Vec3b>(y,x)=cv::Vec3b(shade,shade,shade);
        }
        for(int x:{20,40,60})cv::rectangle(texture,{x,20,6,30},cv::Scalar(255,255,255),cv::FILLED);
        require(measure(texture,{"textured",{5,5,180,90},true,1.0},policy)["contrastOutcome"]=="inconclusive","textured backgrounds must not receive confident contrast");
        // Rendered screenshot of a 50%-opaque black panel over uniform blue.
        // The screenshot is opaque RGB; the panel's alpha is already composited.
        cv::Mat panel(100,200,CV_8UC3,cv::Scalar(60,20,10));
        for(int x:{20,40,60})cv::rectangle(panel,{x,20,6,30},cv::Scalar(255,255,255),cv::FILLED);
        auto panelResult=measure(panel,{"composited-panel",{5,5,180,90},true,1.0},policy);
        require(panelResult["measuredHeightPx"]==30&&panelResult["heightOutcome"]=="pass"&&panelResult["contrastOutcome"]=="pass","opaque screenshot of transparent panel remains measurable");
        cv::Mat multiple(120,300,CV_8UC3,cv::Scalar(0,0,0));
        for(int x:{20,40,60})cv::rectangle(multiple,{x,20,6,20},cv::Scalar(255,255,255),cv::FILLED);
        for(int x:{170,190,210})cv::rectangle(multiple,{x,20,6,40},cv::Scalar(255,255,255),cv::FILLED);
        Policy sizePolicy{"multiple-elements","1",26,4.5};
        require(measure(multiple,{"small",{10,10,80,60},true,1.0},sizePolicy)["heightOutcome"]=="fail","small independent element is not hidden by neighboring large text");
        require(measure(multiple,{"large",{160,10,80,60},true,1.0},sizePolicy)["heightOutcome"]=="pass","large independent element measured separately");
        std::cout << "measurement golden masks passed\n";
    } catch(const std::exception& e) { std::cerr << e.what() << '\n'; return 1; }
}
