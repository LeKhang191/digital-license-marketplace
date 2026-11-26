import hmac
import hashlib
from datetime import datetime, timezone, timedelta
from urllib.parse import urlencode

def params(order, tmn, **overrides):
    p = {
        "vnp_Amount": order["amount"],
        "vnp_BankCode": "NCB",
        "vnp_CardType": "ATM",
        "vnp_OrderInfo": "DevelopmentTest",
        "vnp_PayDate": (
            datetime.now(timezone.utc)
            + timedelta(hours=7)
        ).strftime("%Y%m%d%H%M%S"),
        "vnp_ResponseCode": "00",
        "vnp_TmnCode": tmn,
        "vnp_TransactionNo": order["tx"],
        "vnp_TransactionStatus": "00",
        "vnp_TxnRef": order["ref"],
    }

    p.update(overrides)
    return p

def ipn(request, tmn, secret, order,
        mode="valid", **overrides):

    if tmn != "TEST0001" or not secret:
        raise RuntimeError(
            "Synthetic credentials required"
        )
    p = params(order, tmn, **overrides)
    query = urlencode(sorted(
        (k, v) for k, v in p.items() if v
    ))

    signature = hmac.new(
        secret.encode(),
        query.encode(),
        hashlib.sha512
    ).hexdigest()

    if mode == "invalid":
        signature = "0" * 128

    if mode != "missing":
        query += "&vnp_SecureHash=" + signature

    result = request(
        "GET",
        "/orders/vnpay-ipn?" + query
    )

    return result.get("RspCode")

