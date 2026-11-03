# Deterministic synthetic profile inputs. IDs and audit timestamps are always server-owned.
function New-RealisticDataset([int]$Seed, [DateTimeOffset]$Clock) {
    $random = [Random]::new($Seed)
    $brands = @('Minh Phúc','An Việt','Thành Tín','Nam Hải','Đông Nam','Bình Minh','Hưng Thịnh')
    $sectors = @('Bán lẻ','Logistics','Du lịch','Giáo dục','Sản xuất')
    $needs = @('quản lý khách hàng tại các chi nhánh','đồng bộ dữ liệu bán hàng','theo dõi lịch chăm sóc','chuẩn hóa quy trình tư vấn','phân tích hiệu quả kinh doanh')
    $cities = @('TP. Hồ Chí Minh','Hà Nội','Đà Nẵng','Cần Thơ','Hải Phòng')
    $surnames = @('Nguyễn','Trần','Lê','Phạm','Hoàng','Vũ','Đặng','Bùi','Đỗ','Ngô')
    $given = @('Minh Anh','Quang Huy','Thu Hà','Đức Minh','Ngọc Lan','Hoài Nam','Thanh Trúc','Gia Bảo','Khánh Linh','Tuấn Kiệt','Hải Yến','Anh Khoa','Thảo Vy','Trung Hiếu','Bảo Ngân')
    $sources = @('Website','Referral','Facebook','Google Ads','Event','Direct')
    $services = @('Triển khai CRM','Tích hợp dữ liệu bán hàng','Nâng cấp chăm sóc khách hàng','Đào tạo đội ngũ tư vấn','Gia hạn dịch vụ hỗ trợ','Tư vấn chuyển đổi số')
    $actions = @('Gọi lại để xác nhận nhu cầu','Gửi tài liệu giới thiệu giải pháp','Tổ chức buổi demo nghiệp vụ','Xác nhận phạm vi triển khai','Trao đổi phương án tích hợp','Theo dõi phản hồi đề xuất')
    $organizations = @(for ($i=0; $i -lt 35; $i++) {
        $sector = $sectors[$i % 5]; $brand = $brands[[int][Math]::Floor($i / 5)]
        @{displayName="Công ty $sector $brand"; legalName="Công ty TNHH $sector $brand";
          industry=$sector; employeeCount=(15 + $random.Next(450)); email="doanhnghiep.$i@example.com";
          address="$($cities[$i % 5]) — địa chỉ mô phỏng";
          notes="Doanh nghiệp hư cấu phục vụ SYNTHETIC_DEMO. Ưu tiên $($needs[$i % 5]); triển khai theo từng phòng ban."}
    })
    $contacts = @(for ($i=0; $i -lt 100; $i++) {
        @{fullName="$($surnames[$i % 10]) $($given[[int][Math]::Floor($i / 10)])";
          workEmail="lienhe.$i@example.com"; jobTitle=@('Giám đốc vận hành','Trưởng phòng kinh doanh','Chuyên viên CNTT','Quản lý chi nhánh')[$i % 4];
          needSummary="Cần $($needs[$i % 5]) cho $($organizations[$i % 35].displayName).";
          notes="Liên hệ tổng hợp. Cần bản đề xuất phù hợp quy mô $($organizations[$i % 35].employeeCount) nhân sự.";
          tags=@('SYNTHETIC_DEMO',$sectors[$i % 5])}
    })
    $products = @(for ($i=0; $i -lt 30; $i++) {
        @{sku="AD-$($i.ToString('D3'))"; name="$($services[$i % 6]) - $($sectors[[int][Math]::Floor($i / 6)])";
          type='service'; status='ACTIVE'; category=$services[$i % 6]; unit='gói';
          unitPrice=@{amount=([string](2500000 + $i * 1750000));currency='VND'};
          taxRate='0';taxMode='none';billingCycle='one_time';isSubscription=$false;isRenewable=$false;
          tags=@('SYNTHETIC_DEMO'); description="Gói tư vấn và dịch vụ mô phỏng cho $($sectors[[int][Math]::Floor($i / 6)]). Không tạo giao dịch thanh toán."}
    })
    $leads = @(for ($i=0; $i -lt 150; $i++) {
        @{displayName="$($surnames[$i % 10]) $($given[[int][Math]::Floor($i / 10)])";
          email="tiemnang.$i@example.com";companyName=$organizations[$i % 35].displayName;
          industry=$sectors[$i % 5];source=$sources[$i % 6];preferredChannel='email';doNotCall=$true;doNotEmail=$true;
          priority=@('low','medium','high')[$i % 3];estimatedValue=@{amount=([string](10000000 + $random.Next(900) * 1000000));currency='VND'};
          painPoint="Cần $($needs[$i % 5]); hiện đang xử lý thủ công tại $($cities[$i % 5]).";
          nextFollowUpAt=$Clock.AddDays(($i % 35)-12).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ');
          followUpNote=$actions[$i % 6];tags=@('SYNTHETIC_DEMO',$sectors[$i % 5]);
          description="Nguồn $($sources[$i % 6]); quan tâm $($services[$i % 6]). Dữ liệu tổng hợp, không liên lạc bên ngoài."}
    })
    return @{organizations=$organizations;contacts=$contacts;products=$products;leads=$leads;services=$services;actions=$actions;needs=$needs;sectors=$sectors}
}
