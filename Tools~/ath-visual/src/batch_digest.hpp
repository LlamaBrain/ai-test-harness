#pragma once
#include <array>
#include <bit>
#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

namespace ath::visual {
// SHA-256, FIPS 180-4 sections 4.2.2, 5.1.1, 5.3.3 and 6.2.
// https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.180-4.pdf
// Binds the on-disk batch result to the exact bytes emitted by the trusted worker.
inline std::string batchDigest(std::string_view input) {
    constexpr std::array<uint32_t,64> constants{
        0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
        0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
        0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
        0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
        0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
        0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
        0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
        0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2};
    std::array<uint32_t,8> state{0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19};
    std::vector<uint8_t> bytes(input.begin(),input.end());
    const auto bits=uint64_t(bytes.size())*8;
    bytes.push_back(0x80);
    while(bytes.size()%64!=56) bytes.push_back(0);
    for(int shift=56;shift>=0;shift-=8) bytes.push_back(static_cast<uint8_t>(bits>>shift));
    for(size_t offset=0;offset<bytes.size();offset+=64) {
        std::array<uint32_t,64> schedule{};
        for(size_t i=0;i<16;++i) for(size_t j=0;j<4;++j) schedule[i]=(schedule[i]<<8)|bytes[offset+i*4+j];
        for(size_t i=16;i<64;++i) {
            const auto x=schedule[i-15],y=schedule[i-2];
            schedule[i]=schedule[i-16]+(std::rotr(x,7)^std::rotr(x,18)^(x>>3))+schedule[i-7]+(std::rotr(y,17)^std::rotr(y,19)^(y>>10));
        }
        auto [a,b,c,d,e,f,g,h]=state;
        for(size_t i=0;i<64;++i) {
            const auto first=h+(std::rotr(e,6)^std::rotr(e,11)^std::rotr(e,25))+((e&f)^(~e&g))+constants[i]+schedule[i];
            const auto second=(std::rotr(a,2)^std::rotr(a,13)^std::rotr(a,22))+((a&b)^(a&c)^(b&c));
            h=g;g=f;f=e;e=d+first;d=c;c=b;b=a;a=first+second;
        }
        const std::array<uint32_t,8> block{a,b,c,d,e,f,g,h};
        for(size_t i=0;i<8;++i) state[i]+=block[i];
    }
    constexpr char hex[]="0123456789abcdef";
    std::string digest;digest.reserve(64);
    for(auto value:state) for(int shift=28;shift>=0;shift-=4) digest.push_back(hex[(value>>shift)&15]);
    return digest;
}
}
