#pragma once
#include <opencv2/opencv.hpp>
#include <nlohmann/json.hpp>
#include <string>
#include <memory>
namespace ath::visual {
using Json = nlohmann::json;
struct Policy { std::string id, version; double minHeightPx = 26; double minContrastRatio = 4.5; std::string scope = "default-rendered-size"; };
struct Region { std::string id; cv::Rect bounds; bool representativeGlyphs = false; double detectionConfidence = 0; cv::Rect expectedBounds{}; };
Json measure(const cv::Mat& image, const Region& region, const Policy& policy);
Json analyze(const cv::Mat& image, const Json& manifest, const Policy& policy, const std::string& model);
class AnalysisSession {
public:
    explicit AnalysisSession(const std::string& model = "");
    Json analyze(const cv::Mat& image, const Json& manifest, const Policy& policy);
private:
    std::unique_ptr<cv::dnn::TextDetectionModel_DB> detector_;
};
}
