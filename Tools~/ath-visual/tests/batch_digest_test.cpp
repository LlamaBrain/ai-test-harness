#include "../src/batch_digest.hpp"
#include <iostream>
int main() {
    using ath::visual::batchDigest;
    if(batchDigest("")!="e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" ||
       batchDigest("abc")!="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad" ||
       batchDigest("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq")!="248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1" ||
       batchDigest(std::string(1000000,'a'))!="cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0") return 1;
    std::cout<<"Batch result SHA-256 known vectors passed\n";
}
