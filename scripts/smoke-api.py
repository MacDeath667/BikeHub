#!/usr/bin/env python3
"""Exercise the running mock API; creates one demo bike, post and comment in memory."""
import json
import sys
from urllib.error import HTTPError
from urllib.parse import urlencode
from urllib.request import Request, urlopen

BASE = (sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:5179").rstrip("/")
checks = 0


def request(path, method="GET", body=None, status=200):
    global checks
    req = Request(BASE + "/api" + path, method=method,
                  data=json.dumps(body).encode() if body is not None else None,
                  headers={"Content-Type": "application/json"})
    try:
        response = urlopen(req, timeout=10)
    except HTTPError as error:
        response = error
    with response:
        assert response.status == status, (path, response.status, response.read().decode())
        checks += 1
        return json.loads(response.read() or "null")


assert request("/health")["dataSource"] == "in-memory-mocks"
me = request("/users/me")
feed = request("/feed?pageSize=2")
assert len(feed["items"]) == 2 and feed["hasMore"]
second = request("/feed?pageSize=2&page=2")
assert not {p["id"] for p in feed["items"]} & {p["id"] for p in second["items"]}
request("/feed?page=0", status=400)
request("/bikes?pageSize=51", status=400)
assert request("/feed?page=2147483647&pageSize=50")["items"] == []
request("/posts/00000000-0000-0000-0000-000000000000", status=404)
request("/missing-route", status=404)
post = request("/posts/30000000-0000-0000-0000-000000000001")
post_id = post["id"]
liked = request(f"/posts/{post_id}/like", "PUT")
assert liked["isLiked"]
assert request(f"/posts/{post_id}/like", "PUT")["likes"] == liked["likes"]
request(f"/posts/{post_id}/like", "PUT" if post["isLiked"] else "DELETE")
assert request(f"/posts/{post_id}")["likes"] == post["likes"]
request(f"/posts/{post_id}/bookmark", "PUT")
assert post_id in {p["id"] for p in request("/feed?tab=saved")["items"]}
request(f"/posts/{post_id}/bookmark", "PUT" if post["isSaved"] else "DELETE")
author = request(f'/users/{post["author"]["id"]}')
followed = request(f'/users/{author["id"]}/follow', "PUT")
assert request(f'/users/{author["id"]}/follow', "PUT")["followers"] == followed["followers"]
assert all(p["author"]["isFollowing"] for p in request("/feed?tab=following")["items"])
request(f'/users/{author["id"]}/follow', "PUT" if author["isFollowing"] else "DELETE")
group = request("/communities")[0]
joined = request(f'/communities/{group["id"]}/membership', "PUT")
assert request(f'/communities/{group["id"]}/membership', "PUT")["members"] == joined["members"]
request(f'/communities/{group["id"]}/membership', "PUT" if group["isJoined"] else "DELETE")
request("/bikes", "POST", {"name": " ", "brand": "Canyon", "category": "invalid", "year": 1900, "mileage": -1, "description": ""}, 400)
bike = request("/bikes", "POST", {"name": "Smoke Test Gravel", "brand": "Canyon", "category": "gravel", "year": 2024, "mileage": 120, "description": "Тестовый велосипед"}, 201)
assert bike["ownerId"] == me["id"]
assert request(f'/users/{me["id"]}')["bikesCount"] == me["bikesCount"] + 1
assert bike["id"] in {b["id"] for b in request(f'/users/{me["id"]}/bikes?pageSize=50')["items"]}
created = request("/posts", "POST", {"bikeId": bike["id"], "title": "Проверка нового бортжурнала", "content": "Первая запись для проверки сквозного сценария API.", "category": "ride"}, 201)
assert request(f'/bikes/{bike["id"]}/history')["items"][0]["id"] == created["id"]
assert request("/feed")["items"][0]["id"] == created["id"]
request("/posts", "POST", {"bikeId": post["bike"]["id"], "title": "Чужой велосипед", "content": "Нельзя публиковать записи от имени другого владельца.", "category": "story"}, 400)
request(f'/posts/{created["id"]}/comments', "POST", {"text": " "}, 400)
comment = request(f'/posts/{created["id"]}/comments', "POST", {"text": "Первый комментарий из smoke-теста."}, 201)
assert request(f'/posts/{created["id"]}')["commentsCount"] == 1
assert request(f'/posts/{created["id"]}/comments')["items"][0]["id"] == comment["id"]
assert created["id"] in {p["id"] for p in request("/feed?" + urlencode({"q": "Проверка нового бортжурнала"}))["items"]}
assert all(p["bike"]["category"] == "gravel" for p in request("/feed?category=gravel")["items"])
popular = request("/feed?tab=popular")["items"]
assert [p["likes"] for p in popular] == sorted((p["likes"] for p in popular), reverse=True)
print(f"OK: {checks} HTTP checks; pagination, validation, likes, bookmarks, follows, communities, garage, posts, comments, search.")
