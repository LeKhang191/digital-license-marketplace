import json

from urllib.error import HTTPError


def api_result(request, method, path, token=None, data=None):
    try:
        return 200, request(method, path, token, data)

    except HTTPError as exc:
        try:
            body = json.loads(exc.read())
        except (ValueError, UnicodeError):
            body = {}

        return exc.code, body
